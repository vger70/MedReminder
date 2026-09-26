using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Timeline;

// Read model of the therapy timeline view (EVOLUTION-PROPOSALS §4.3):
// one row per medicine over a date window, with typed segments and
// markers. Built by TherapyTimelineBuilder; rendered by the UI.

// Inclusive date range shown by the timeline.
public readonly record struct TimelineWindow
{
    public const int DefaultDaysBack = 60;
    public const int DefaultDaysForward = 120;

    public DateOnly Start { get; }
    public DateOnly End { get; }

    public TimelineWindow(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentException("Timeline window end precedes its start.", nameof(end));
        }
        Start = start;
        End = end;
    }

    // Number of days in the window, both ends included.
    public int Days => End.DayNumber - Start.DayNumber + 1;

    public static TimelineWindow Around(
        DateOnly today,
        int daysBack = DefaultDaysBack,
        int daysForward = DefaultDaysForward)
        => new(today.AddDays(-daysBack), today.AddDays(daysForward));

    public bool Contains(DateOnly day) => day >= Start && day <= End;

    public TimelineWindow Shift(int days) => new(Start.AddDays(days), End.AddDays(days));
}

public enum TimelineSegmentKind
{
    Active = 0,
    Suspended = 1,
}

// A run of consecutive days in the same state, clipped to the window.
// Start and End are inclusive. ContinuesBeforeWindow / After tell
// whether the same state extends past the window edge. Suspensions
// lists the suspension records that overlap a Suspended segment (more
// than one when suspensions overlap); it is empty for Active segments.
public sealed record TimelineSegment(
    TimelineSegmentKind Kind,
    DateOnly Start,
    DateOnly End,
    bool ContinuesBeforeWindow,
    bool ContinuesAfterWindow,
    IReadOnlyList<MedicationSuspension> Suspensions);

public enum TimelineMarkerKind
{
    // First schedule entry of the therapy.
    InitialSchedule = 0,
    // A later schedule entry: dose, frequency or regimen changed.
    ScheduleChange = 1,
    // Start of stage 2..n inside a stepped taper.
    TaperStage = 2,
    // End of a bounded stepped taper (the rate drops to zero).
    TaperCourseEnd = 3,
}

// Point event on a row. Schedule is the regimen in force from Date.
// StageNumber / StageCount / StageDose are set for TaperStage only.
public sealed record TimelineMarker(
    DateOnly Date,
    TimelineMarkerKind Kind,
    Schedule Schedule,
    int? StageNumber = null,
    int? StageCount = null,
    decimal? StageDose = null);

public sealed record TherapyTimelineRow(
    Guid MedicineId,
    string Name,
    string Unit,
    bool IsActive,
    DateOnly TherapyStart,
    DateOnly? TherapyEnd,
    IReadOnlyList<TimelineSegment> Segments,
    IReadOnlyList<TimelineMarker> Markers,
    Schedule? ScheduleToday,
    bool HasAdministrationSlots,
    decimal CurrentStock,
    MedicineForecastResult Forecast)
{
    // The run-out estimate is drawn only for medicines still tracked:
    // a deactivated medicine keeps its table value but gets no marker.
    public DateOnly? RunOutDateToDraw
        => IsActive ? Forecast.RunOut.EstimatedRunOutDate : null;

    public bool RunOutFallsAfterTherapyEnd
        => TherapyEnd is { } end
           && Forecast.RunOut.EstimatedRunOutDate is { } runOut
           && runOut > end;
}

public sealed record TherapyTimeline(
    DateOnly Today,
    TimelineWindow Window,
    IReadOnlyList<TherapyTimelineRow> Rows);

// Everything the builder needs about one medicine. CurrentStock is the
// value computed by MedicineStock.Current.
public sealed record TherapyTimelineInput(
    Medicine Medicine,
    IReadOnlyList<MedicationScheduleHistory> ScheduleHistory,
    IReadOnlyList<MedicationSuspension> Suspensions,
    IReadOnlyList<MedicationAdministrationSlot> AdministrationSlots,
    decimal CurrentStock);
