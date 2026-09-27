using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Ledger;

// B.1 Phase 2c-2: the use cases record facts and LedgerSynchronizer /
// LedgerFactsLoader turn them into the stored ledger
// (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.3, §13).
public class LedgerWiringTests
{
    // ApplicationTestScope clock: 2026-09-13 12:00 UTC, local = UTC.
    private static readonly DateOnly Today = new(2026, 9, 13);

    private readonly ApplicationTestScope _scope = new();

    private Task<Guid> SeedAsync(DateOnly? start = null)
        => _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 1,
                start ?? new DateOnly(2026, 9, 3), 7,
                NotificationChannels.Windows,
                InitialQuantity: 50m),
            CancellationToken.None);

    private Task UpdateActiveAsync(Guid id, bool active)
        => _scope.UpdateMedicine.ExecuteAsync(
            new UpdateMedicineCommand(
                id, "Enalapril", null, null, "compresse", 7,
                NotificationChannels.Windows, null, null, null, IsActive: active),
            CancellationToken.None);

    private async Task<decimal> StockAsync(Guid id)
        => MedicineStock.Current(await _scope.Stock.ListForMedicineAsync(id, CancellationToken.None));

    [Fact]
    public async Task Activity_changes_are_recorded_only_when_the_value_changes()
    {
        var id = await SeedAsync();

        await UpdateActiveAsync(id, true);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), CancellationToken.None);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), CancellationToken.None);
        _scope.Clock.AdvanceBy(TimeSpan.FromHours(1));
        await UpdateActiveAsync(id, true);

        _scope.Activity.All.Select(a => (a.Day, a.Active)).Should().Equal((Today, false), (Today, true));
        _scope.Activity.All[1].RecordedAt.Should().Be(_scope.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Schedule_rows_carry_their_recording_instant()
    {
        var id = await SeedAsync();
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(3));

        await _scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 1m, 2, Today.AddDays(1)), CancellationToken.None);

        var rows = await _scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        rows.Select(r => r.RecordedAt).Should().Equal(
            _scope.Clock.GetUtcNow().AddMinutes(-3), _scope.Clock.GetUtcNow());
    }

    [Fact]
    public async Task A_count_is_stored_as_a_fact_with_its_outcome()
    {
        var id = await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var result = await _scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 38m, TakenToday: 1m, Notes: " counted "), CancellationToken.None);

        var count = _scope.Counts.All.Should().ContainSingle().Subject;
        count.CountDay.Should().Be(Today);
        count.CountedQuantity.Should().Be(38m);
        count.TakenToday.Should().Be(1m);
        count.LedgerAtStartOfDay.Should().Be(40m);
        count.CountDayScheduled.Should().Be(1m);
        count.Correction.Should().Be(result.Correction).And.Be(-1m);
        count.MaterializesCountDay.Should().BeTrue();
        count.Notes.Should().Be("counted");
        (await StockAsync(id)).Should().Be(38m);

        (await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None)).Should().Be(0);
        (await StockAsync(id)).Should().Be(38m);
    }

    [Fact]
    public async Task A_count_without_a_gap_is_still_recorded()
    {
        var id = await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        var result = await _scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(id, 40m, TakenToday: 0m), CancellationToken.None);

        result.CorrectionKind.Should().BeNull();
        _scope.Counts.All.Should().ContainSingle().Which.Correction.Should().Be(0m);
    }

    [Fact]
    public async Task Inactive_medicines_are_derived_too()
    {
        var id = await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), CancellationToken.None);
        _scope.Clock.AdvanceBy(TimeSpan.FromDays(3));

        (await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None)).Should().Be(0);
        (await StockAsync(id)).Should().Be(40m, "no consumption while inactive, rows already booked stay");
    }

    [Fact]
    public async Task An_inactive_medicine_without_history_is_inactive_after_the_cutoff()
    {
        var id = await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var medicine = await _scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.IsActive = false;   // as data written before the activity history existed
        _scope.FreezeLedger();
        _scope.Activity.All.Should().ContainSingle();

        _scope.Clock.AdvanceBy(TimeSpan.FromDays(3));
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await StockAsync(id)).Should().Be(40m);
    }

    [Fact]
    public async Task A_medicine_created_after_the_freeze_is_derived_from_its_start()
    {
        _scope.FreezeLedger();
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        var id = await SeedAsync(start: new DateOnly(2026, 9, 3));

        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await StockAsync(id)).Should().Be(40m, "3..12 September, backdated start after the freeze");
    }

    [Fact]
    public async Task Refills_after_the_freeze_count_on_the_baseline_epoch()
    {
        var id = await SeedAsync();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        _scope.FreezeLedger();
        _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(1));

        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await _scope.Medicines.GetAsync(id, CancellationToken.None))!.StockEpoch.Should().Be(3);
    }

    [Fact]
    public async Task Legacy_intakes_derive_nothing_after_the_freeze()
    {
        var id = await SeedAsync();
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, Today, IntakeStatus.Taken, 1m), CancellationToken.None);
        var before = await StockAsync(id);

        _scope.FreezeLedger();
        _scope.Clock.AdvanceBy(TimeSpan.FromDays(1));
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        // Day 13 carries a legacy intake: not booked again, no automatic row.
        (await StockAsync(id)).Should().Be(before);
    }
}
