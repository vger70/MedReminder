using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Ledger;

// B.1 Phase 2d (D8): retraction of mistaken facts. Rule (product owner,
// 2026-09-27): only facts recorded after the ledger freeze and after the
// medicine's latest stock count.
public class FactRetractionTests
{
    // Clock: 2026-09-13 12:00 UTC. Medicine from 3 September, 1/day,
    // 50 initial: 40 at the start of the 13th.
    private readonly ApplicationTestScope _scope = new();
    private Guid _id;

    private async Task SeedAsync()
    {
        _id = await _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 3), 7,
                NotificationChannels.Windows, InitialQuantity: 50m),
            CancellationToken.None);
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
    }

    private void Later() => _scope.Clock.AdvanceBy(TimeSpan.FromMinutes(5));

    private async Task<decimal> StockAsync()
        => MedicineStock.Current(await _scope.Stock.ListForMedicineAsync(_id, CancellationToken.None));

    private async Task<Medicine> MedicineAsync()
        => (await _scope.Medicines.GetAsync(_id, CancellationToken.None))!;

    private async Task<FactHistoryItem> ItemAsync(FactKind kind, Func<FactHistoryItem, bool>? match = null)
        => (await _scope.FactHistory.LoadAsync(_id, CancellationToken.None))
            .First(i => i.Kind == kind && (match?.Invoke(i) ?? true));

    private Task RetractAsync(FactHistoryItem item)
        => _scope.RetractFact.ExecuteAsync(new RetractFactCommand(_id, item.Kind, item.FactId), CancellationToken.None);

    [Fact]
    public async Task Retracting_a_refill_restores_stock_and_epoch()
    {
        await SeedAsync();
        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        (await MedicineAsync()).StockEpoch.Should().Be(2);

        var refill = await ItemAsync(FactKind.StockEntry, i => i.MovementKind == StockMovementKind.NewPackage);
        refill.CanRetract.Should().BeTrue();
        Later();
        await RetractAsync(refill);

        (await StockAsync()).Should().Be(40m);
        var medicine = await MedicineAsync();
        medicine.StockEpoch.Should().Be(1);
        _scope.Stock.All.Should().NotContain(m => m.Id == refill.FactId);
        _scope.Retractions.All.Should().ContainSingle(r => r.FactId == refill.FactId && r.Kind == FactKind.StockEntry);
    }

    [Fact]
    public async Task A_fact_before_a_later_count_cannot_be_retracted()
    {
        await SeedAsync();
        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        Later();
        await _scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(_id, 40m, TakenToday: 0m), CancellationToken.None);

        var refill = await ItemAsync(FactKind.StockEntry, i => i.MovementKind == StockMovementKind.NewPackage);
        refill.Block.Should().Be(RetractionBlock.LaterCount);
        await FluentActions.Awaiting(() => RetractAsync(refill)).Should().ThrowAsync<InvalidOperationException>();
        (await StockAsync()).Should().Be(40m);

        (await ItemAsync(FactKind.StockCount)).CanRetract.Should().BeTrue("the latest count itself can be retracted");
    }

    [Fact]
    public async Task Retracting_the_latest_count_removes_its_correction()
    {
        await SeedAsync();
        Later();
        await _scope.ReconcileStock.ExecuteAsync(
            new ReconcileStockCommand(_id, 30m, TakenToday: 0m), CancellationToken.None);
        (await StockAsync()).Should().Be(30m);

        Later();
        await RetractAsync(await ItemAsync(FactKind.StockCount));

        (await StockAsync()).Should().Be(40m);
        _scope.Counts.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Retracting_a_backdated_intake_brings_back_the_automatic_consumption()
    {
        await SeedAsync();
        Later();
        await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(_id, new DateOnly(2026, 9, 10), IntakeStatus.Taken, 3m), CancellationToken.None);
        (await StockAsync()).Should().Be(38m);

        Later();
        await RetractAsync(await ItemAsync(FactKind.Intake));

        (await StockAsync()).Should().Be(40m);
        _scope.Intakes.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Retracting_a_suspension_books_its_days_again()
    {
        await SeedAsync();
        Later();
        await _scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(_id, new DateOnly(2026, 9, 13)), CancellationToken.None);
        _scope.Clock.AdvanceBy(TimeSpan.FromDays(3));
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        (await StockAsync()).Should().Be(40m, "suspended from the 13th");

        await RetractAsync(await ItemAsync(FactKind.Suspension));

        (await StockAsync()).Should().Be(37m, "13..15 September booked");
    }

    [Fact]
    public async Task Facts_recorded_before_the_freeze_are_not_retractable()
    {
        await SeedAsync();
        await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(_id, new DateOnly(2026, 9, 13), IntakeStatus.Taken, 1m), CancellationToken.None);
        _scope.FreezeLedger();

        var history = await _scope.FactHistory.LoadAsync(_id, CancellationToken.None);

        history.Should().ContainSingle(i => i.Kind == FactKind.Intake)
            .Which.Block.Should().Be(RetractionBlock.Legacy);
        history.Should().NotContain(i => i.Kind == FactKind.StockEntry, "the initial load is a Legacy row now");
    }

    [Fact]
    public async Task History_lists_facts_newest_first_and_no_derived_rows()
    {
        await SeedAsync();
        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        Later();
        await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(_id, new DateOnly(2026, 9, 13), IntakeStatus.Skipped, 1m), CancellationToken.None);

        var history = await _scope.FactHistory.LoadAsync(_id, CancellationToken.None);

        history.Select(i => i.Kind).Should().Equal(FactKind.Intake, FactKind.StockEntry, FactKind.StockEntry);
        history.Last().MovementKind.Should().Be(StockMovementKind.InitialLoad);
    }

    [Fact]
    public async Task The_epoch_fact_id_follows_the_opening_fact()
    {
        await SeedAsync();
        var baseline = (await MedicineAsync()).StockEpochFactId;
        baseline.Should().NotBeNull();

        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var refill = _scope.Stock.All.Single(m => m.Kind == StockMovementKind.NewPackage);
        (await MedicineAsync()).StockEpochFactId.Should().Be(refill.Id);

        Later();
        await RetractAsync(await ItemAsync(FactKind.StockEntry, i => i.FactId == refill.Id));
        (await MedicineAsync()).StockEpochFactId.Should().Be(baseline);
    }

    // A warning sent in an epoch whose refill is then retracted must not
    // suppress the warning of the next refill, although both get the
    // same epoch number.
    [Fact]
    public async Task A_reused_epoch_number_does_not_suppress_a_due_warning()
    {
        await SeedAsync();
        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 1m, StockMovementKind.NewPackage), CancellationToken.None);
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);
        var medicine = await MedicineAsync();
        await _scope.Notifications.AddAsync(new NotificationEvent
        {
            MedicineId = _id,
            StockEpoch = medicine.StockEpoch,
            EpochFactId = medicine.StockEpochFactId,
            TriggeredAt = _scope.Clock.GetUtcNow(),
            Channel = NotificationChannels.Windows,
            DaysRemainingAtSend = 5,
            Success = true,
        }, CancellationToken.None);

        Later();
        await RetractAsync(await ItemAsync(FactKind.StockEntry, i => i.MovementKind == StockMovementKind.NewPackage));
        Later();
        await _scope.AddStock.ExecuteAsync(
            new AddStockCommand(_id, 1m, StockMovementKind.NewPackage), CancellationToken.None);
        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        medicine = await MedicineAsync();
        var latest = await _scope.Notifications.GetLatestForMedicineAsync(_id, CancellationToken.None);
        latest!.StockEpoch.Should().Be(medicine.StockEpoch, "the number is reused");
        NotificationCycle.ShouldNotify(medicine, daysRemaining: 3, estimatedRunOutDate: null, latest)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Older_events_get_the_epoch_fact_id_on_the_first_derivation()
    {
        await SeedAsync();
        var medicine = await MedicineAsync();
        await _scope.Notifications.AddAsync(new NotificationEvent
        {
            MedicineId = _id,
            StockEpoch = medicine.StockEpoch,
            TriggeredAt = _scope.Clock.GetUtcNow(),
            Channel = NotificationChannels.Windows,
            DaysRemainingAtSend = 5,
            Success = true,
        }, CancellationToken.None);
        var expected = medicine.StockEpochFactId;
        medicine.StockEpochFactId = null;   // as a database upgraded to 2d

        await _scope.ConsumptionCatchUp.RunAsync(CancellationToken.None);

        (await _scope.Notifications.GetLatestForMedicineAsync(_id, CancellationToken.None))!
            .EpochFactId.Should().Be(expected);
    }
}
