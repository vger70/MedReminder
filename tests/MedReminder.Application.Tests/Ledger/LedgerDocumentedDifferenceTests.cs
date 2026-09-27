using FluentAssertions;
using MedReminder.Domain.Calculations;
using Xunit;

namespace MedReminder.Application.Tests.Ledger;

// Cases where the derived ledger deliberately differs from today's
// behavior (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §18.9, D6, D15,
// §17). Same figures as spike S9: 50 units initial stock, 1 unit/day
// unless stated. They become user-visible in Phase 2c-2.
public sealed class LedgerDocumentedDifferenceTests
{
    private static (decimal Oracle, decimal Derived) Stocks(LedgerParityHarness h, Guid id)
    {
        h.Oracle.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
        var oracle = MedicineStock.Current(h.Oracle.Stock.ListForMedicineAsync(id, default).GetAwaiter().GetResult());
        return (oracle, h.Derive(id).Stock);
    }

    // D6: a schedule change dated in the past is re-applied to the days
    // the catch-up already booked.
    [Fact]
    public void Retroactive_schedule_change_is_re_derived()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 1, 50m, 5, null, null);
        var id = h.Medicines[0];
        for (var d = 1; d <= 10; d++) h.StartDay(d);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.ChangeSchedule(id, h.Today.AddDays(-5), 1m, 2);

        var (oracle, derived) = Stocks(h, id);
        oracle.Should().Be(40m);   // 10 days at 1/day
        derived.Should().Be(35m);  // days -5..-1 now at 2/day
    }

    // D15: the inactive period of a reactivated medicine is not booked.
    [Fact]
    public void Reactivation_does_not_book_the_inactive_period()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 1, 50m, 5, null, null);
        var id = h.Medicines[0];
        h.StartDay(1);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Update(id, active: false);
        for (var d = 2; d <= 8; d++) h.StartDay(d);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Update(id, active: true);

        var (oracle, derived) = Stocks(h, id);
        oracle.Should().Be(42m);   // days 0..7 booked
        derived.Should().Be(49m);  // only day 0 booked; days 1..7 inactive
    }

    // §17: of two schedule rows with the same EffectiveFrom, the later
    // recorded one wins (today the first one does).
    [Fact]
    public void Same_date_schedule_change_takes_the_latest()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 1, 50m, 5, null, null);
        var id = h.Medicines[0];
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.ChangeSchedule(id, h.Today.AddDays(1), 1m, 2);
        h.AdvanceTo(TimeSpan.FromHours(10));
        h.ChangeSchedule(id, h.Today.AddDays(1), 1m, 3);
        for (var d = 1; d <= 3; d++) h.StartDay(d);

        var (oracle, derived) = Stocks(h, id);
        oracle.Should().Be(45m);   // day 0 at 1, days 1..2 at 2 (first row)
        derived.Should().Be(43m);  // day 0 at 1, days 1..2 at 3 (latest row)
    }

    // D6 before the cutoff: days re-opened by clearing a past end date
    // stay frozen; only days after the cutoff are derived.
    [Fact]
    public void Reopened_days_before_the_cutoff_stay_frozen()
    {
        var h = new LedgerParityHarness();
        h.StartDay(0);
        h.AddMedicine(h.Today, 1m, 1, 50m, 5, h.Today.AddDays(2), null);
        var id = h.Medicines[0];
        for (var d = 1; d <= 6; d++) h.StartDay(d);
        h.ApplyCutoffPatch();
        h.StartDay(7);
        h.AdvanceTo(TimeSpan.FromHours(9));
        h.Update(id, setEnd: true, end: null);

        var (oracle, derived) = Stocks(h, id);
        oracle.Should().Be(43m);   // days 0..2, then 3..6 re-opened (frozen) and day 6 after cutoff
        derived.Should().Be(46m);  // days 0..2 (legacy) and day 6 (after cutoff)
    }
}
