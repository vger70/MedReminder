using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Timeline;

// Pure builder of the therapy timeline: no repositories, no clock, no
// WinForms. Reuses the domain rules instead of re-implementing them:
//  - SuspensionState decides whether a day is suspended;
//  - ScheduleCodec rebuilds each schedule entry;
//  - MedicineForecast gives the same run-out date as the main table.
//
// Days are classified one by one inside the window and run-length
// encoded into segments, so overlapping or open-ended suspensions need
// no special casing. Past consumption is not reconstructed.
public static class TherapyTimelineBuilder
{
    private enum DayState
    {
        None,
        Active,
        Suspended,
    }

    public static TherapyTimeline Build(
        IEnumerable<TherapyTimelineInput> inputs,
        DateOnly today,
        TimelineWindow window)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var rows = inputs
            .Select(input => BuildRow(input, today, window))
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new TherapyTimeline(today, window, rows);
    }

    public static TherapyTimelineRow BuildRow(
        TherapyTimelineInput input,
        DateOnly today,
        TimelineWindow window)
    {
        ArgumentNullException.ThrowIfNull(input);
        var medicine = input.Medicine;

        var forecast = MedicineForecast.Compute(
            today, input.CurrentStock, input.ScheduleHistory,
            input.AdministrationSlots, input.Suspensions);

        var history = input.ScheduleHistory
            .OrderBy(h => h.EffectiveFrom)
            .ToList();

        return new TherapyTimelineRow(
            MedicineId: medicine.Id,
            Name: medicine.Name,
            Unit: medicine.Unit,
            IsActive: medicine.IsActive,
            TherapyStart: medicine.StartDate,
            TherapyEnd: medicine.EndDate,
            Segments: BuildSegments(medicine, input.Suspensions, today, window),
            Markers: BuildMarkers(history, window),
            ScheduleToday: ScheduleInForce(history, today),
            HasAdministrationSlots: input.AdministrationSlots.Count > 0,
            CurrentStock: input.CurrentStock,
            Forecast: forecast);
    }

    private static IReadOnlyList<TimelineSegment> BuildSegments(
        Medicine medicine,
        IReadOnlyList<MedicationSuspension> suspensions,
        DateOnly today,
        TimelineWindow window)
    {
        // A deactivated medicine has no recorded deactivation date: its
        // span is cut at today so it does not look ongoing in the future.
        DateOnly? lastDay = medicine.EndDate;
        if (!medicine.IsActive && (lastDay is null || lastDay > today))
        {
            lastDay = today;
        }

        DayState StateOn(DateOnly day)
        {
            if (day < medicine.StartDate) return DayState.None;
            if (lastDay is { } end && day > end) return DayState.None;
            return SuspensionState.IsSuspendedOn(day, suspensions)
                ? DayState.Suspended
                : DayState.Active;
        }

        var segments = new List<TimelineSegment>();
        var runStart = window.Start;
        var runState = StateOn(window.Start);

        for (var day = window.Start; ; day = day.AddDays(1))
        {
            var isLast = day == window.End;
            var nextState = isLast ? DayState.None : StateOn(day.AddDays(1));
            if (isLast || nextState != runState)
            {
                if (runState != DayState.None)
                {
                    segments.Add(CreateSegment(
                        runState, runStart, day, window, suspensions, StateOn));
                }
                if (isLast) break;
                runStart = day.AddDays(1);
                runState = nextState;
            }
        }

        return segments;
    }

    private static TimelineSegment CreateSegment(
        DayState state,
        DateOnly start,
        DateOnly end,
        TimelineWindow window,
        IReadOnlyList<MedicationSuspension> suspensions,
        Func<DateOnly, DayState> stateOn)
    {
        var continuesBefore = start == window.Start
            && start > DateOnly.MinValue
            && stateOn(start.AddDays(-1)) == state;
        var continuesAfter = end == window.End
            && end < DateOnly.MaxValue
            && stateOn(end.AddDays(1)) == state;

        IReadOnlyList<MedicationSuspension> covering = state == DayState.Suspended
            ? suspensions
                .Where(s => s.StartDate <= end && (s.EndDate is null || s.EndDate >= start))
                .OrderBy(s => s.StartDate)
                .ToList()
            : [];

        var kind = state == DayState.Suspended
            ? TimelineSegmentKind.Suspended
            : TimelineSegmentKind.Active;
        return new TimelineSegment(kind, start, end, continuesBefore, continuesAfter, covering);
    }

    private static IReadOnlyList<TimelineMarker> BuildMarkers(
        IReadOnlyList<MedicationScheduleHistory> history,
        TimelineWindow window)
    {
        var markers = new List<TimelineMarker>();
        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            // A later entry supersedes this one from its EffectiveFrom:
            // stages of a taper that was replaced are not drawn.
            DateOnly? supersededFrom = i + 1 < history.Count
                ? history[i + 1].EffectiveFrom
                : null;
            var schedule = Deserialize(entry);

            if (window.Contains(entry.EffectiveFrom))
            {
                markers.Add(new TimelineMarker(
                    entry.EffectiveFrom,
                    i == 0 ? TimelineMarkerKind.InitialSchedule : TimelineMarkerKind.ScheduleChange,
                    schedule));
            }

            if (schedule is SteppedTaperingSchedule stepped)
            {
                AddTaperMarkers(markers, stepped, entry.EffectiveFrom, supersededFrom, window);
            }
        }

        return markers.OrderBy(m => m.Date).ToList();
    }

    private static void AddTaperMarkers(
        List<TimelineMarker> markers,
        SteppedTaperingSchedule stepped,
        DateOnly anchor,
        DateOnly? supersededFrom,
        TimelineWindow window)
    {
        bool Visible(DateOnly day)
            => window.Contains(day) && (supersededFrom is null || day < supersededFrom);

        var cursor = anchor;
        var count = stepped.Stages.Count;
        for (var s = 0; s < count; s++)
        {
            var stage = stepped.Stages[s];
            if (s > 0 && Visible(cursor))
            {
                markers.Add(new TimelineMarker(
                    cursor, TimelineMarkerKind.TaperStage, stepped,
                    StageNumber: s + 1, StageCount: count, StageDose: stage.Dose));
            }
            // A maintained last stage never ends.
            if (s == count - 1 && stepped.MaintainLastDose) return;
            cursor = cursor.AddDays(stage.DurationDays);
        }

        if (Visible(cursor))
        {
            markers.Add(new TimelineMarker(cursor, TimelineMarkerKind.TaperCourseEnd, stepped));
        }
    }

    private static Schedule? ScheduleInForce(
        IReadOnlyList<MedicationScheduleHistory> orderedHistory,
        DateOnly day)
    {
        // Same tie rule as DailyConsumption.RateOn: on equal dates the
        // first entry wins.
        MedicationScheduleHistory? latest = null;
        foreach (var entry in orderedHistory)
        {
            if (entry.EffectiveFrom > day) break;
            if (latest is null || entry.EffectiveFrom > latest.EffectiveFrom)
            {
                latest = entry;
            }
        }
        return latest is null ? null : Deserialize(latest);
    }

    private static Schedule Deserialize(MedicationScheduleHistory entry)
        => ScheduleCodec.Deserialize(
            entry.ScheduleKind,
            entry.SchedulePayload,
            entry.DosePerAdministration,
            entry.AdministrationsPerDay);
}
