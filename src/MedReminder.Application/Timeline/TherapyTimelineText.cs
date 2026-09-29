using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Timeline;

// Localized wording of the timeline: tooltips, the details pane and
// the accessible names use the same sentences, so the information is
// never available only as a drawing. Run-out dates are always worded
// as estimates.
public sealed class TherapyTimelineText
{
    private const string P = "Ui.TherapyTimeline.";
    private static readonly string NL = Environment.NewLine;

    private readonly ILocalizationService _loc;
    private readonly CultureInfo _culture;

    public TherapyTimelineText(ILocalizationService localization)
    {
        _loc = localization ?? throw new ArgumentNullException(nameof(localization));
        _culture = localization.CurrentCulture;
    }

    public CultureInfo Culture => _culture;

    public string InactiveTag => _loc.Get(P + "Row.InactiveTag");

    public string Date(DateOnly day) => day.ToString("d", _culture);

    public string Quantity(decimal value) => value.ToString("0.##", _culture);

    public string DescribeSchedule(Schedule schedule, string unit)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return schedule switch
        {
            FixedDailySchedule f => _loc.Get(P + "Schedule.FixedDaily",
                Quantity(f.DosePerAdministration), unit, f.AdministrationsPerDay),
            WeeklySchedule w => _loc.Get(P + "Schedule.Weekly",
                Quantity(w.QuantitiesByDayOfWeek.Sum()), unit),
            CyclicSchedule c => _loc.Get(P + "Schedule.Cyclic",
                Quantity(c.QuantityPerOnDay), unit, c.OnDays, c.OffDays),
            TaperingSchedule t => _loc.Get(P + "Schedule.Tapering",
                Quantity(t.StartDose), Quantity(t.EndDose), unit, Quantity(t.Step), t.IntervalDays),
            PrnSchedule => _loc.Get(P + "Schedule.Prn"),
            SteppedTaperingSchedule s => DescribeStepped(s, unit),
            _ => schedule.Kind.ToString(),
        };
    }

    private string DescribeStepped(SteppedTaperingSchedule s, string unit)
    {
        var stages = string.Join(" → ", s.Stages.Select(stage =>
            _loc.Get(P + "Schedule.Stage", Quantity(stage.Dose), unit, stage.DurationDays)));
        var text = _loc.Get(P + "Schedule.Stepped", stages);
        return s.MaintainLastDose
            ? text + ", " + _loc.Get(P + "Schedule.Stepped.Maintain")
            : text;
    }

    public string TherapySpan(TherapyTimelineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.TherapyEnd is { } end
            ? _loc.Get(P + "Row.Therapy.Bounded", Date(row.TherapyStart), Date(end))
            : _loc.Get(P + "Row.Therapy.OpenEnded", Date(row.TherapyStart));
    }

    public string DescribeSegment(TherapyTimelineRow row, TimelineSegment segment)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Kind == TimelineSegmentKind.Suspended)
        {
            return string.Join(NL, segment.Suspensions.Select(DescribeSuspension));
        }

        var key = (segment.ContinuesBeforeWindow, segment.ContinuesAfterWindow) switch
        {
            (true, true) => "Segment.Active.Throughout",
            (true, false) => "Segment.Active.ContinuesBefore",
            (false, true) => "Segment.Active.ContinuesAfter",
            _ => "Segment.Active",
        };
        return _loc.Get(P + key, Date(segment.Start), Date(segment.End));
    }

    public string DescribeSuspension(MedicationSuspension suspension)
    {
        ArgumentNullException.ThrowIfNull(suspension);
        var text = suspension.EndDate is { } end
            ? _loc.Get(P + "Segment.Suspended.Bounded", Date(suspension.StartDate), Date(end))
            : _loc.Get(P + "Segment.Suspended.Open", Date(suspension.StartDate));
        return string.IsNullOrWhiteSpace(suspension.Reason)
            ? text
            : text + " — " + _loc.Get(P + "Segment.Reason", suspension.Reason.Trim());
    }

    public string DescribeMarker(TherapyTimelineRow row, TimelineMarker marker)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(marker);
        var date = Date(marker.Date);
        return marker.Kind switch
        {
            TimelineMarkerKind.InitialSchedule => _loc.Get(P + "Marker.Initial",
                date, DescribeSchedule(marker.Schedule, row.Unit)),
            TimelineMarkerKind.ScheduleChange => _loc.Get(P + "Marker.Change",
                date, DescribeSchedule(marker.Schedule, row.Unit)),
            TimelineMarkerKind.TaperStage => _loc.Get(P + "Marker.TaperStage",
                date, marker.StageNumber ?? 0, marker.StageCount ?? 0,
                Quantity(marker.StageDose ?? 0m), row.Unit),
            TimelineMarkerKind.TaperCourseEnd => _loc.Get(P + "Marker.TaperCourseEnd", date),
            _ => date,
        };
    }

    public string DescribeRunOut(TherapyTimelineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var runOut = row.Forecast.RunOut;
        if (runOut.EstimatedRunOutDate is { } eta)
        {
            var text = _loc.Get(P + "RunOut.Estimate", Date(eta), runOut.DaysRemaining ?? 0);
            return row.RunOutFallsAfterTherapyEnd
                ? text + " " + _loc.Get(P + "RunOut.AfterEnd")
                : text;
        }
        return row.Forecast.IsSuspendedToday
            ? _loc.Get(P + "RunOut.NoneSuspended")
            : _loc.Get(P + "RunOut.NoneNoRate");
    }

    // One line, for the accessible name of a row.
    public string Summarize(TherapyTimelineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var parts = new List<string> { row.Name, TherapySpan(row) };
        if (!row.IsActive) parts.Add(InactiveTag);
        if (row.Forecast.IsSuspendedToday) parts.Add(_loc.Get(P + "Row.SuspendedToday"));
        if (row.IsActive) parts.Add(DescribeRunOut(row));
        return string.Join(". ", parts.Select(p => p.TrimEnd('.')));
    }

    // Full text of a row for the details pane and the row tooltip.
    public string DescribeRow(TherapyTimelineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var sb = new StringBuilder();

        sb.Append(row.Name);
        if (!row.IsActive) sb.Append(' ').Append(InactiveTag);
        sb.Append(NL);
        sb.Append(TherapySpan(row)).Append(NL);

        sb.Append(row.ScheduleToday is { } schedule
            ? _loc.Get(P + "Row.CurrentSchedule", DescribeSchedule(schedule, row.Unit))
            : _loc.Get(P + "Row.NoSchedule")).Append(NL);
        if (row.HasAdministrationSlots)
        {
            sb.Append(_loc.Get(P + "Row.SlotsNote",
                Quantity(row.Forecast.DailyRate), row.Unit)).Append(NL);
        }
        sb.Append(_loc.Get(P + "Row.Stock", Quantity(row.CurrentStock), row.Unit)).Append(NL);

        if (row.Forecast.IsSuspendedToday)
        {
            sb.Append(_loc.Get(P + "Row.SuspendedToday")).Append(NL);
        }
        sb.Append(row.IsActive ? DescribeRunOut(row) : _loc.Get(P + "Row.Inactive")).Append(NL);

        sb.Append(NL).Append(_loc.Get(P + "Row.Section.Suspensions")).Append(NL);
        var suspensions = row.Segments
            .SelectMany(s => s.Suspensions)
            .Distinct()
            .OrderBy(s => s.StartDate)
            .ToList();
        AppendList(sb, suspensions.Select(DescribeSuspension));

        sb.Append(NL).Append(_loc.Get(P + "Row.Section.Changes")).Append(NL);
        AppendList(sb, row.Markers.Select(m => DescribeMarker(row, m)));

        return sb.ToString().TrimEnd();
    }

    private void AppendList(StringBuilder sb, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append("  - ").Append(line).Append(NL);
            any = true;
        }
        if (!any)
        {
            sb.Append("  - ").Append(_loc.Get(P + "Row.None")).Append(NL);
        }
    }
}
