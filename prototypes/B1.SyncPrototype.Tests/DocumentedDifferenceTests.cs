using FluentAssertions;
using MedReminder.Domain.Calculations;
using Xunit;

namespace MedReminder.Prototypes.Sync.Tests;

// Cases where the derived ledger deliberately differs from today's
// behavior. Each test pins the size of the difference so the spike
// results in ANALYSIS-B1-MOBILE-SYNC.md §18 can quote it.
public sealed class DocumentedDifferenceTests
{
    private static (decimal Oracle, decimal Derived) Stocks(ParityHarness h, Guid id)
    {
        h.Oracle.ConsumptionCatchUp.RunAsync(default).GetAwaiter().GetResult();
        var oracle = MedicineStock.Current(h.Oracle.Stock.ListForMedicineAsync(id, default).GetAwaiter().GetResult());
        var derived = LedgerDeriver.Derive(h.Proto, id, h.Today).Stock;
        return (oracle, derived);
    }

    // D6: a schedule change dated in the past. Today the catch-up never
    // revisits days it already booked; the derivation re-applies the new
    // rate to them.
    [Fact]
    public void Retroactive_schedule_change_is_re_derived()
    {
        var h = new ParityHarness();
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

    // D15: reactivation. Today the catch-up books the whole inactive
    // period at once; the derivation books nothing for inactive days.
    [Fact]
    public void Reactivation_does_not_book_the_inactive_period()
    {
        var h = new ParityHarness();
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

    // §17: two schedule rows with the same EffectiveFrom. Today the first
    // row wins (strict '>' in DailyConsumption); the derivation keeps the
    // most recently recorded one.
    [Fact]
    public void Same_date_schedule_change_takes_the_latest()
    {
        var h = new ParityHarness();
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

    // D6 before the cutoff: clearing an end date that is already past.
    // Today the catch-up books the re-opened days, frozen ones included;
    // the derivation never touches frozen days, only days after cutoff.
    [Fact]
    public void Reopened_days_before_the_cutoff_stay_frozen()
    {
        var h = new ParityHarness();
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
