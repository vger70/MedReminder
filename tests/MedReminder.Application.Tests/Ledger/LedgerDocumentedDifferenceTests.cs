using FluentAssertions;
using MedReminder.Domain.Calculations;
using Xunit;

namespace MedReminder.Application.Tests.Ledger;

// Behavior changes that the ledger derivation brings (docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §18.9, D6, D15, §17), user-visible since
// Phase 2c-2. Same figures as spike S9: 50 units initial stock, 1
// unit/day unless stated; the comment gives the figure before 2c-2. The
// stored ledger and a fresh derivation from the harness's facts must
// agree.
public sealed class LedgerDocumentedDifferenceTests
{
    private static (decimal App, decimal Derived) Stocks(LedgerParityHarness h, Guid id)
    {
        h.App.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
        var oracle = MedicineStock.Current(h.App.Stock.ListForMedicineAsync(id, default).GetAwaiter().GetResult());
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

        var (stored, derived) = Stocks(h, id);
        stored.Should().Be(35m);   // days -5..-1 re-derived at 2/day (was 40)
        derived.Should().Be(35m);
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

        var (stored, derived) = Stocks(h, id);
        stored.Should().Be(49m);   // only day 0 booked; days 1..7 inactive (was 42)
        derived.Should().Be(49m);
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

        var (stored, derived) = Stocks(h, id);
        stored.Should().Be(43m);   // day 0 at 1, days 1..2 at 3, latest row (was 45)
        derived.Should().Be(43m);
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

        var (stored, derived) = Stocks(h, id);
        stored.Should().Be(46m);   // days 0..2 (legacy) and day 6 after the cutoff (was 43)
        derived.Should().Be(46m);
    }
}
