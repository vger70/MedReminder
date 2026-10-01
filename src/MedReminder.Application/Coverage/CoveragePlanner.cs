using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Coverage;

// Coverage planner (docs/notes/EVOLUTION-PROPOSALS-2.md §3.5): for a
// period chosen by the user (a trip, the next pharmacy visit), how much
// of each active medicine the period needs, how much will be left when
// it starts, and how much is missing.
//
// Pure: no repositories, no clock. The daily quantities come from
// ConsumptionMaterializer, the rule that writes the automatic
// consumption, so the plan follows the therapy window, suspensions,
// schedule changes and slots exactly as the stock will. Automatic
// consumption is recorded up to yesterday, so today's planned use is
// still to come and is subtracted before the period like any later day;
// an intake already registered today makes the estimate slightly
// cautious, never optimistic. Writes nothing.
public static class CoveragePlanner
{
    // Longest period accepted, in days (inclusive of both ends).
    public const int MaxDays = 366;

    public static CoveragePlan Build(
        IEnumerable<CoverageInput> inputs,
        DateOnly today,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (from < today)
        {
            throw new ArgumentOutOfRangeException(nameof(from), "The period cannot start before today.");
        }
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), "The period cannot end before it starts.");
        }
        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            throw new ArgumentOutOfRangeException(nameof(to), $"The period cannot be longer than {MaxDays} days.");
        }

        var rows = inputs
            .Where(i => i.Medicine.IsActive)
            .Select(i => BuildRow(i, today, from, to))
            .OrderBy(r => r.Status)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new CoveragePlan(today, from, to, rows);
    }

    public static CoverageRow BuildRow(CoverageInput input, DateOnly today, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(input);
        var medicine = input.Medicine;

        var before = from > today
            ? Sum(ConsumptionMaterializer.Plan(medicine, today, from.AddDays(-1),
                input.ScheduleHistory, input.Suspensions, input.AdministrationSlots))
            : 0m;
        var needed = Sum(ConsumptionMaterializer.Plan(medicine, from, to,
            input.ScheduleHistory, input.Suspensions, input.AdministrationSlots));

        var stockAtStart = input.CurrentStock - before;
        var shortfall = Math.Max(0m, needed - Math.Max(0m, stockAtStart));
        int? packages = shortfall > 0m && input.PackageQuantity is > 0m and var size
            ? (int)decimal.Ceiling(shortfall / size)
            : null;

        var status = needed > 0m
            ? shortfall > 0m ? CoverageStatus.Short : CoverageStatus.Covered
            : IsAsNeeded(input, to) ? CoverageStatus.AsNeeded : CoverageStatus.NotInUse;

        return new CoverageRow(
            MedicineId: medicine.Id,
            Name: medicine.Name,
            ActiveIngredient: medicine.ActiveIngredient,
            Unit: medicine.Unit,
            CurrentStock: input.CurrentStock,
            StockAtStart: stockAtStart,
            Needed: needed,
            Shortfall: shortfall,
            PackageQuantity: input.PackageQuantity,
            Packages: packages,
            Status: status);
    }

    private static decimal Sum(IReadOnlyList<MaterializedConsumption> days)
    {
        var total = 0m;
        foreach (var day in days) total += day.Quantity;
        return total;
    }

    // An as-needed (PRN) medicine has no planned use: the plan cannot say
    // how much the period needs. Slots take precedence over the schedule,
    // as in DailyConsumption.
    private static bool IsAsNeeded(CoverageInput input, DateOnly to)
    {
        if (input.AdministrationSlots.Count > 0) return false;
        MedicationScheduleHistory? inForce = null;
        foreach (var row in input.ScheduleHistory)
        {
            if (row.EffectiveFrom > to) continue;
            if (inForce is null || row.EffectiveFrom >= inForce.EffectiveFrom) inForce = row;
        }
        return inForce?.ScheduleKind == ScheduleKind.Prn;
    }
}

// What the planner reads for one medicine. PackageQuantity is the
// quantity of its last new package, when one was recorded.
public sealed record CoverageInput(
    Medicine Medicine,
    IReadOnlyList<MedicationScheduleHistory> ScheduleHistory,
    IReadOnlyList<MedicationSuspension> Suspensions,
    IReadOnlyList<MedicationAdministrationSlot> AdministrationSlots,
    decimal CurrentStock,
    decimal? PackageQuantity);

// Order matters: the plan lists the medicines to get first.
public enum CoverageStatus
{
    Short,
    Covered,
    AsNeeded,
    NotInUse,
}

// StockAtStart may be negative when the stock runs out before the
// period; Shortfall counts only what the period itself needs. Packages
// is the number of packages of PackageQuantity that cover the
// shortfall, null when nothing is missing or no package was recorded.
public sealed record CoverageRow(
    Guid MedicineId,
    string Name,
    string? ActiveIngredient,
    string Unit,
    decimal CurrentStock,
    decimal StockAtStart,
    decimal Needed,
    decimal Shortfall,
    decimal? PackageQuantity,
    int? Packages,
    CoverageStatus Status);

public sealed record CoveragePlan(
    DateOnly Today,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CoverageRow> Rows)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public int ShortCount => Rows.Count(r => r.Status == CoverageStatus.Short);
}
