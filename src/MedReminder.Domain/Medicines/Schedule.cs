namespace MedReminder.Domain.Medicines;

// Value object describing HOW MUCH of a medicine is consumed on a
// given day. Six discriminated shapes; only FixedDaily is used by
// pre-A1 databases via the ScheduleCodec fallback.
//
// See docs/ANALYSIS-A1-REGIMENS.md §2 for the base design and
// docs/ANALYSIS-A1-STEPPED-TAPER.md for the multi-stage taper.
public abstract record Schedule
{
    public ScheduleKind Kind { get; }

    protected Schedule(ScheduleKind kind) => Kind = kind;

    // Quantity to consume on `day`. `anchor` is the EffectiveFrom of
    // the enclosing MedicationScheduleHistory entry — cyclic and
    // tapering schedules use it as their pattern origin; weekly and
    // fixed schedules ignore it; PRN always returns zero.
    public abstract decimal RateOn(DateOnly day, DateOnly anchor);
}

public sealed record FixedDailySchedule : Schedule
{
    public decimal DosePerAdministration { get; }
    public int AdministrationsPerDay { get; }

    public FixedDailySchedule(decimal dosePerAdministration, int administrationsPerDay)
        : base(ScheduleKind.FixedDaily)
    {
        if (dosePerAdministration <= 0m)
        {
            throw new ArgumentException(
                "Dose per administration must be positive.",
                nameof(dosePerAdministration));
        }
        if (administrationsPerDay <= 0)
        {
            throw new ArgumentException(
                "Administrations per day must be at least 1.",
                nameof(administrationsPerDay));
        }
        DosePerAdministration = dosePerAdministration;
        AdministrationsPerDay = administrationsPerDay;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
        => DosePerAdministration * AdministrationsPerDay;
}

public sealed record WeeklySchedule : Schedule
{
    // Seven quantities keyed by day of week, indexed Monday = 0 …
    // Sunday = 6. Every entry must be >= 0; at least one must be > 0
    // (a schedule where every day is zero is a de-facto PRN and must
    // be modeled as PrnSchedule instead).
    public IReadOnlyList<decimal> QuantitiesByDayOfWeek { get; }

    public WeeklySchedule(IReadOnlyList<decimal> quantitiesByDayOfWeek)
        : base(ScheduleKind.Weekly)
    {
        ArgumentNullException.ThrowIfNull(quantitiesByDayOfWeek);
        if (quantitiesByDayOfWeek.Count != 7)
        {
            throw new ArgumentException(
                "Weekly schedule requires exactly 7 quantities (Mon..Sun).",
                nameof(quantitiesByDayOfWeek));
        }
        var totalPositive = 0m;
        for (var i = 0; i < 7; i++)
        {
            if (quantitiesByDayOfWeek[i] < 0m)
            {
                throw new ArgumentException(
                    "Weekly schedule quantities cannot be negative.",
                    nameof(quantitiesByDayOfWeek));
            }
            totalPositive += quantitiesByDayOfWeek[i];
        }
        if (totalPositive <= 0m)
        {
            throw new ArgumentException(
                "Weekly schedule must have at least one positive day; use PrnSchedule for an all-zero pattern.",
                nameof(quantitiesByDayOfWeek));
        }
        // Defensive copy so the caller cannot mutate the payload
        // after construction.
        var buffer = new decimal[7];
        for (var i = 0; i < 7; i++) buffer[i] = quantitiesByDayOfWeek[i];
        QuantitiesByDayOfWeek = buffer;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
    {
        // System.DayOfWeek is Sunday = 0 .. Saturday = 6; remap so
        // Monday = 0 to match the payload layout.
        var index = ((int)day.DayOfWeek + 6) % 7;
        return QuantitiesByDayOfWeek[index];
    }

    // Records auto-generate equality from declared properties.
    // IReadOnlyList<decimal> uses reference equality, which is wrong
    // for two structurally identical schedules — override both.
    public bool Equals(WeeklySchedule? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.Kind != Kind) return false;
        for (var i = 0; i < 7; i++)
        {
            if (QuantitiesByDayOfWeek[i] != other.QuantitiesByDayOfWeek[i]) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        for (var i = 0; i < 7; i++) hash.Add(QuantitiesByDayOfWeek[i]);
        return hash.ToHashCode();
    }
}

public sealed record CyclicSchedule : Schedule
{
    public int OnDays { get; }
    public int OffDays { get; }
    public decimal QuantityPerOnDay { get; }

    public CyclicSchedule(int onDays, int offDays, decimal quantityPerOnDay)
        : base(ScheduleKind.Cyclic)
    {
        if (onDays < 1)
        {
            throw new ArgumentException(
                "Cyclic schedule requires at least one on-day.",
                nameof(onDays));
        }
        if (offDays < 0)
        {
            throw new ArgumentException(
                "Cyclic schedule off-days cannot be negative.",
                nameof(offDays));
        }
        if (quantityPerOnDay <= 0m)
        {
            throw new ArgumentException(
                "Cyclic schedule quantity per on-day must be positive.",
                nameof(quantityPerOnDay));
        }
        OnDays = onDays;
        OffDays = offDays;
        QuantityPerOnDay = quantityPerOnDay;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
    {
        if (day < anchor) return 0m;
        var period = OnDays + OffDays;
        if (period <= 0) return 0m;
        var dayInCycle = (day.DayNumber - anchor.DayNumber) % period;
        return dayInCycle < OnDays ? QuantityPerOnDay : 0m;
    }
}

public sealed record TaperingSchedule : Schedule
{
    public decimal StartDose { get; }
    public decimal EndDose { get; }
    public decimal Step { get; }
    public int IntervalDays { get; }

    public TaperingSchedule(decimal startDose, decimal endDose, decimal step, int intervalDays)
        : base(ScheduleKind.Tapering)
    {
        if (startDose <= 0m)
        {
            throw new ArgumentException(
                "Tapering schedule start dose must be positive.",
                nameof(startDose));
        }
        if (endDose < 0m)
        {
            throw new ArgumentException(
                "Tapering schedule end dose cannot be negative.",
                nameof(endDose));
        }
        if (step <= 0m)
        {
            throw new ArgumentException(
                "Tapering schedule step must be positive.",
                nameof(step));
        }
        if (intervalDays < 1)
        {
            throw new ArgumentException(
                "Tapering schedule interval must be at least 1 day.",
                nameof(intervalDays));
        }
        if (startDose == endDose)
        {
            throw new ArgumentException(
                "Tapering schedule start and end doses cannot be equal; use FixedDailySchedule for a constant dose.",
                nameof(endDose));
        }
        StartDose = startDose;
        EndDose = endDose;
        Step = step;
        IntervalDays = intervalDays;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
    {
        if (day < anchor) return 0m;
        if (IntervalDays <= 0) return 0m;
        var intervals = (day.DayNumber - anchor.DayNumber) / IntervalDays;
        var descending = StartDose > EndDose;
        var candidate = descending
            ? StartDose - Step * intervals
            : StartDose + Step * intervals;
        candidate = descending
            ? decimal.Max(candidate, EndDose)
            : decimal.Min(candidate, EndDose);
        return candidate < 0m ? 0m : candidate;
    }
}

public sealed record PrnSchedule : Schedule
{
    public PrnSchedule() : base(ScheduleKind.Prn) { }

    public override decimal RateOn(DateOnly day, DateOnly anchor) => 0m;
}

// One stage of a stepped taper: a flat dose held for a whole number of
// consecutive days. See docs/ANALYSIS-A1-STEPPED-TAPER.md §2.1.
public sealed record TaperStage
{
    public decimal Dose { get; }
    public int DurationDays { get; }

    public TaperStage(decimal dose, int durationDays)
    {
        if (dose <= 0m)
        {
            throw new ArgumentException(
                "Taper stage dose must be positive.",
                nameof(dose));
        }
        if (durationDays < 1)
        {
            throw new ArgumentException(
                "Taper stage duration must be at least 1 day.",
                nameof(durationDays));
        }
        Dose = dose;
        DurationDays = durationDays;
    }
}

// Multi-stage (stepped) taper: an ordered sequence of stages, each a
// flat dose held for its own number of days. Unlike TaperingSchedule
// (a single constant step every fixed interval), the doses and
// durations of each stage are arbitrary, so it can express regimens
// such as "4/day for 7 days, then 2/day for 7 days, then 1/day for
// 14 days". See docs/ANALYSIS-A1-STEPPED-TAPER.md §2.1.
public sealed record SteppedTaperingSchedule : Schedule
{
    public IReadOnlyList<TaperStage> Stages { get; }

    // When true, the last stage's DurationDays is ignored and its dose
    // is held indefinitely (a maintenance regime). When false, the
    // course ends once the last stage's days elapse and the rate
    // returns to zero.
    public bool MaintainLastDose { get; }

    public SteppedTaperingSchedule(
        IReadOnlyList<TaperStage> stages,
        bool maintainLastDose = false)
        : base(ScheduleKind.SteppedTapering)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count < 2)
        {
            throw new ArgumentException(
                "Stepped tapering requires at least two stages; use FixedDailySchedule for a single constant dose.",
                nameof(stages));
        }
        // Defensive copy so the caller cannot mutate the payload after
        // construction (same discipline as WeeklySchedule). Each stage
        // has already validated its own invariants in its constructor.
        var buffer = new TaperStage[stages.Count];
        for (var i = 0; i < stages.Count; i++)
        {
            buffer[i] = stages[i] ?? throw new ArgumentException(
                "Stepped tapering stages cannot be null.", nameof(stages));
        }
        Stages = buffer;
        MaintainLastDose = maintainLastDose;
    }

    public override decimal RateOn(DateOnly day, DateOnly anchor)
    {
        if (day < anchor) return 0m;
        var elapsed = day.DayNumber - anchor.DayNumber; // 0-based
        var cursor = 0;
        for (var i = 0; i < Stages.Count; i++)
        {
            var stage = Stages[i];
            var isLast = i == Stages.Count - 1;
            // The last stage, when MaintainLastDose is set, holds its
            // dose for any day at or past its start.
            if (isLast && MaintainLastDose)
            {
                return stage.Dose;
            }
            if (elapsed < cursor + stage.DurationDays)
            {
                return stage.Dose;
            }
            cursor += stage.DurationDays;
        }
        // Past the end of a bounded course: the therapy is over.
        return 0m;
    }

    // Records auto-generate equality from declared properties.
    // IReadOnlyList<TaperStage> uses reference equality, which is wrong
    // for two structurally identical schedules — override both, as
    // WeeklySchedule does.
    public bool Equals(SteppedTaperingSchedule? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (other.Kind != Kind) return false;
        if (other.MaintainLastDose != MaintainLastDose) return false;
        if (other.Stages.Count != Stages.Count) return false;
        for (var i = 0; i < Stages.Count; i++)
        {
            if (Stages[i] != other.Stages[i]) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(MaintainLastDose);
        foreach (var stage in Stages) hash.Add(stage);
        return hash.ToHashCode();
    }
}
