using FluentAssertions;
using MedReminder.Application.Timeline;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Timeline;

public class TherapyTimelineBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    // 2026-07-15 .. 2027-01-11
    private static readonly TimelineWindow Window = TimelineWindow.Around(Today);

    private static Medicine NewMedicine(
        DateOnly start, DateOnly? end = null, bool isActive = true, string name = "Enalapril")
        => new()
        {
            Name = name,
            Unit = "tablets",
            StartDate = start,
            EndDate = end,
            IsActive = isActive,
            DosePerAdministration = 1m,
            AdministrationsPerDay = 2,
        };

    private static MedicationScheduleHistory Fixed(Medicine m, DateOnly from, decimal dose = 1m, int admin = 2)
        => new()
        {
            MedicineId = m.Id,
            EffectiveFrom = from,
            DosePerAdministration = dose,
            AdministrationsPerDay = admin,
        };

    private static MedicationScheduleHistory WithSchedule(Medicine m, DateOnly from, Schedule schedule)
    {
        var (kind, payload) = ScheduleCodec.Serialize(schedule);
        return new MedicationScheduleHistory
        {
            MedicineId = m.Id,
            EffectiveFrom = from,
            DosePerAdministration = 1m,
            AdministrationsPerDay = 1,
            ScheduleKind = kind,
            SchedulePayload = payload,
        };
    }

    private static MedicationSuspension Suspension(Medicine m, DateOnly start, DateOnly? end, string? reason = null)
        => new() { MedicineId = m.Id, StartDate = start, EndDate = end, Reason = reason };

    private static TherapyTimelineRow Row(
        Medicine m,
        IReadOnlyList<MedicationScheduleHistory>? history = null,
        IReadOnlyList<MedicationSuspension>? suspensions = null,
        IReadOnlyList<MedicationAdministrationSlot>? slots = null,
        decimal stock = 30m,
        TimelineWindow? window = null)
        => TherapyTimelineBuilder.BuildRow(
            new TherapyTimelineInput(
                m,
                history ?? [Fixed(m, m.StartDate)],
                suspensions ?? [],
                slots ?? [],
                stock),
            Today,
            window ?? Window);

    [Fact]
    public void Default_window_is_60_days_back_and_120_forward()
    {
        Window.Start.Should().Be(new DateOnly(2026, 7, 15));
        Window.End.Should().Be(new DateOnly(2027, 1, 11));
        Window.Days.Should().Be(181);
        Window.Shift(30).Start.Should().Be(new DateOnly(2026, 8, 14));
    }

    [Fact]
    public void Window_rejects_end_before_start()
    {
        var act = () => new TimelineWindow(Today, Today.AddDays(-1));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_ended_therapy_started_before_window_spans_the_whole_window()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));

        var row = Row(m);

        var segment = row.Segments.Should().ContainSingle().Subject;
        segment.Kind.Should().Be(TimelineSegmentKind.Active);
        segment.Start.Should().Be(Window.Start);
        segment.End.Should().Be(Window.End);
        segment.ContinuesBeforeWindow.Should().BeTrue();
        segment.ContinuesAfterWindow.Should().BeTrue();
        segment.Suspensions.Should().BeEmpty();
        row.TherapyEnd.Should().BeNull();
    }

    [Fact]
    public void End_date_inside_window_closes_the_active_segment()
    {
        var m = NewMedicine(new DateOnly(2026, 8, 10), end: new DateOnly(2026, 10, 31));

        var row = Row(m);

        var segment = row.Segments.Should().ContainSingle().Subject;
        segment.Start.Should().Be(new DateOnly(2026, 8, 10));
        segment.End.Should().Be(new DateOnly(2026, 10, 31));
        segment.ContinuesBeforeWindow.Should().BeFalse();
        segment.ContinuesAfterWindow.Should().BeFalse();
    }

    [Fact]
    public void Overlapping_suspensions_merge_into_one_segment_listing_both()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));
        var first = Suspension(m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10), "surgery");
        var second = Suspension(m, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 20));

        var row = Row(m, suspensions: [second, first]);

        row.Segments.Select(s => (s.Kind, s.Start, s.End)).Should().Equal(
            (TimelineSegmentKind.Active, Window.Start, new DateOnly(2026, 8, 31)),
            (TimelineSegmentKind.Suspended, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20)),
            (TimelineSegmentKind.Active, new DateOnly(2026, 9, 21), Window.End));
        row.Segments[1].Suspensions.Should().Equal(first, second);
        row.Segments[1].ContinuesBeforeWindow.Should().BeFalse();
        row.Segments[1].ContinuesAfterWindow.Should().BeFalse();
    }

    [Fact]
    public void Open_ended_suspension_runs_to_the_window_end_and_hides_the_forecast()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));
        var open = Suspension(m, new DateOnly(2026, 9, 10), null);

        var row = Row(m, suspensions: [open]);

        var last = row.Segments[^1];
        last.Kind.Should().Be(TimelineSegmentKind.Suspended);
        last.Start.Should().Be(new DateOnly(2026, 9, 10));
        last.End.Should().Be(Window.End);
        last.ContinuesAfterWindow.Should().BeTrue();
        row.Forecast.IsSuspendedToday.Should().BeTrue();
        row.Forecast.RunOut.EstimatedRunOutDate.Should().BeNull();
        row.RunOutDateToDraw.Should().BeNull();
    }

    [Fact]
    public void Suspension_started_before_the_window_continues_before_it()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));
        var s = Suspension(m, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        var row = Row(m, suspensions: [s]);

        var first = row.Segments[0];
        first.Kind.Should().Be(TimelineSegmentKind.Suspended);
        first.Start.Should().Be(Window.Start);
        first.End.Should().Be(new DateOnly(2026, 7, 31));
        first.ContinuesBeforeWindow.Should().BeTrue();
        row.Segments[1].ContinuesBeforeWindow.Should().BeFalse();
    }

    [Fact]
    public void Stepped_taper_with_several_schedule_changes_yields_ordered_markers()
    {
        var m = NewMedicine(new DateOnly(2026, 8, 1));
        var taper = new SteppedTaperingSchedule(
        [
            new TaperStage(4m, 7),
            new TaperStage(2m, 7),
            new TaperStage(1m, 14),
        ]);
        var history = new[]
        {
            Fixed(m, new DateOnly(2026, 8, 1)),
            WithSchedule(m, new DateOnly(2026, 9, 1), taper),
            Fixed(m, new DateOnly(2026, 10, 15), dose: 1m, admin: 1),
        };

        var row = Row(m, history: history);

        row.Markers.Select(x => (x.Date, x.Kind, x.StageNumber, x.StageDose)).Should().Equal(
            (new DateOnly(2026, 8, 1), TimelineMarkerKind.InitialSchedule, (int?)null, (decimal?)null),
            (new DateOnly(2026, 9, 1), TimelineMarkerKind.ScheduleChange, null, null),
            (new DateOnly(2026, 9, 8), TimelineMarkerKind.TaperStage, 2, 2m),
            (new DateOnly(2026, 9, 15), TimelineMarkerKind.TaperStage, 3, 1m),
            (new DateOnly(2026, 9, 29), TimelineMarkerKind.TaperCourseEnd, null, null),
            (new DateOnly(2026, 10, 15), TimelineMarkerKind.ScheduleChange, null, null));
        row.Markers.Where(x => x.Kind == TimelineMarkerKind.TaperStage)
            .Should().OnlyContain(x => x.StageCount == 3);
        row.ScheduleToday.Should().Be(taper);
        // Rate today (stage 2 of the taper) drives the forecast.
        row.Forecast.DailyRate.Should().Be(2m);
    }

    [Fact]
    public void Taper_stages_after_a_superseding_change_are_not_drawn()
    {
        var m = NewMedicine(new DateOnly(2026, 8, 1));
        var taper = new SteppedTaperingSchedule(
        [
            new TaperStage(4m, 7),
            new TaperStage(2m, 7),
            new TaperStage(1m, 14),
        ]);
        var history = new[]
        {
            WithSchedule(m, new DateOnly(2026, 9, 1), taper),
            Fixed(m, new DateOnly(2026, 9, 10), dose: 1m, admin: 1),
        };

        var row = Row(m, history: history);

        row.Markers.Select(x => (x.Date, x.Kind)).Should().Equal(
            (new DateOnly(2026, 9, 1), TimelineMarkerKind.InitialSchedule),
            (new DateOnly(2026, 9, 8), TimelineMarkerKind.TaperStage),
            (new DateOnly(2026, 9, 10), TimelineMarkerKind.ScheduleChange));
    }

    [Fact]
    public void Maintained_taper_has_no_course_end()
    {
        var m = NewMedicine(new DateOnly(2026, 9, 1));
        var taper = new SteppedTaperingSchedule(
            [new TaperStage(2m, 7), new TaperStage(1m, 7)], maintainLastDose: true);

        var row = Row(m, history: [WithSchedule(m, new DateOnly(2026, 9, 1), taper)]);

        row.Markers.Should().NotContain(x => x.Kind == TimelineMarkerKind.TaperCourseEnd);
        row.Markers.Should().ContainSingle(x => x.Kind == TimelineMarkerKind.TaperStage);
    }

    [Fact]
    public void Inactive_medicine_stops_at_today_and_draws_no_run_out()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1), isActive: false);

        var row = Row(m);

        row.IsActive.Should().BeFalse();
        var segment = row.Segments.Should().ContainSingle().Subject;
        segment.End.Should().Be(Today);
        segment.ContinuesAfterWindow.Should().BeFalse();
        // Same value as the table, but not drawn.
        row.Forecast.RunOut.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 28));
        row.RunOutDateToDraw.Should().BeNull();
    }

    [Fact]
    public void Inactive_medicine_with_past_end_date_keeps_that_end()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1), end: new DateOnly(2026, 8, 20), isActive: false);

        var row = Row(m);

        row.Segments.Should().ContainSingle().Which.End.Should().Be(new DateOnly(2026, 8, 20));
    }

    [Fact]
    public void Rows_list_active_medicines_first_then_by_name()
    {
        var b = NewMedicine(new DateOnly(2026, 1, 1), name: "Bisoprolol");
        var a = NewMedicine(new DateOnly(2026, 1, 1), name: "Atorvastatin");
        var z = NewMedicine(new DateOnly(2026, 1, 1), isActive: false, name: "Aspirin");
        TherapyTimelineInput Input(Medicine m) => new(m, [Fixed(m, m.StartDate)], [], [], 10m);

        var timeline = TherapyTimelineBuilder.Build([Input(z), Input(b), Input(a)], Today, Window);

        timeline.Rows.Select(r => r.Name).Should().Equal("Atorvastatin", "Bisoprolol", "Aspirin");
        timeline.Today.Should().Be(Today);
        timeline.Window.Should().Be(Window);
    }

    [Fact]
    public void Therapy_starting_on_the_window_start_does_not_continue_before()
    {
        var m = NewMedicine(Window.Start, end: Window.End);

        var segment = Row(m).Segments.Should().ContainSingle().Subject;

        segment.ContinuesBeforeWindow.Should().BeFalse();
        segment.ContinuesAfterWindow.Should().BeFalse();
    }

    [Fact]
    public void Therapy_ending_the_day_after_the_window_continues_after()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1), end: Window.End.AddDays(1));

        Row(m).Segments.Should().ContainSingle().Which.ContinuesAfterWindow.Should().BeTrue();
    }

    [Fact]
    public void Therapy_outside_the_window_has_no_segments()
    {
        var before = NewMedicine(new DateOnly(2026, 1, 1), end: Window.Start.AddDays(-1));
        var after = NewMedicine(Window.End.AddDays(1));

        Row(before).Segments.Should().BeEmpty();
        Row(after).Segments.Should().BeEmpty();
    }

    [Fact]
    public void Markers_on_the_window_edges_are_kept_and_outside_ones_dropped()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));
        var history = new[]
        {
            Fixed(m, Window.Start.AddDays(-1)),
            Fixed(m, Window.Start, admin: 3),
            Fixed(m, Window.End, admin: 1),
            Fixed(m, Window.End.AddDays(1), admin: 4),
        };

        var row = Row(m, history: history);

        row.Markers.Select(x => x.Date).Should().Equal(Window.Start, Window.End);
        row.Markers.Should().OnlyContain(x => x.Kind == TimelineMarkerKind.ScheduleChange);
    }

    [Fact]
    public void Suspension_ending_on_the_window_end_does_not_continue_after()
    {
        var m = NewMedicine(new DateOnly(2026, 1, 1));
        var s = Suspension(m, new DateOnly(2026, 12, 1), Window.End);

        var last = Row(m, suspensions: [s]).Segments[^1];

        last.Kind.Should().Be(TimelineSegmentKind.Suspended);
        last.ContinuesAfterWindow.Should().BeFalse();
    }

    [Fact]
    public void Forecast_matches_the_main_table_formula()
    {
        var m = NewMedicine(new DateOnly(2026, 9, 1));
        var history = new[] { Fixed(m, new DateOnly(2026, 9, 1)) };
        var suspensions = new[] { Suspension(m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)) };
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 1m },
            new MedicationAdministrationSlot { MedicineId = m.Id, Dose = 0.5m },
        };

        var row = Row(m, history, suspensions, slots, stock: 20m);

        // MedicineOverviewLoader: RateOn(today, schedule, slots),
        // IsSuspendedOn(today), RunOutForecast.Compute.
        var rate = DailyConsumption.RateOn(Today, history, slots);
        var expected = RunOutForecast.Compute(
            Today, 20m, rate, SuspensionState.IsSuspendedOn(Today, suspensions));
        row.Forecast.RunOut.Should().Be(expected);
        row.Forecast.RunOut.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 26));
        row.RunOutDateToDraw.Should().Be(expected.EstimatedRunOutDate);
        row.HasAdministrationSlots.Should().BeTrue();
    }

    [Fact]
    public void Run_out_after_the_planned_end_is_flagged()
    {
        var m = NewMedicine(new DateOnly(2026, 9, 1), end: new DateOnly(2026, 9, 20));

        var row = Row(m, stock: 30m);

        row.Forecast.RunOut.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 28));
        row.RunOutFallsAfterTherapyEnd.Should().BeTrue();
    }
}
