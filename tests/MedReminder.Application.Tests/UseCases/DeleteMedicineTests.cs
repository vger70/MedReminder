using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// A medicine without recorded facts can be deleted with its
// configuration and derived rows; one with facts cannot.
public class DeleteMedicineTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);

    private readonly ApplicationTestScope _scope = new();

    private Task<Guid> SeedAsync(decimal initialQuantity = 0m)
        => _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows,
            InitialQuantity: initialQuantity,
            AdministrationSlots: [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)]), default);

    private Task<DeleteMedicineOutcome> DeleteAsync(Guid id)
        => _scope.DeleteMedicine.ExecuteAsync(new DeleteMedicineCommand(id), default);

    [Fact]
    public async Task A_medicine_without_facts_is_deleted_with_its_configuration_and_derived_rows()
    {
        var id = await SeedAsync();
        await _scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 10)), default);
        await _scope.ConsumptionCatchUp.RunAsync(default);
        _scope.Stock.All.Should().Contain(m => m.MedicineId == id && m.Origin == StockMovementOrigin.Derived);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default);
        var other = await _scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Other", "compresse", 1m, 1, Start, 7, NotificationChannels.Windows, InitialQuantity: 10m), default);

        (await _scope.DeleteMedicine.CheckAsync(id, default)).Should().Be(DeleteMedicineOutcome.Deleted);
        (await DeleteAsync(id)).Should().Be(DeleteMedicineOutcome.Deleted);

        (await _scope.Medicines.GetAsync(id, default)).Should().BeNull();
        _scope.Slots.All.Should().NotContain(s => s.MedicineId == id);
        (await _scope.Slots.ListSetsForMedicineAsync(id, default)).Should().BeEmpty();
        (await _scope.Schedules.ListForMedicineAsync(id, default)).Should().BeEmpty();
        _scope.Activity.All.Should().NotContain(a => a.MedicineId == id);
        _scope.Stock.All.Should().NotContain(m => m.MedicineId == id);
        (await _scope.Medicines.GetAsync(other, default)).Should().NotBeNull();
        _scope.Stock.All.Should().Contain(m => m.MedicineId == other);
    }

    [Fact]
    public async Task A_medicine_with_a_stock_entry_is_kept_until_the_entry_is_retracted()
    {
        var id = await SeedAsync(initialQuantity: 30m);

        (await _scope.DeleteMedicine.CheckAsync(id, default)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);
        (await DeleteAsync(id)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);
        (await _scope.Medicines.GetAsync(id, default)).Should().NotBeNull();

        var entry = (await _scope.FactHistory.LoadAsync(id, default)).Single();
        await _scope.RetractFact.ExecuteAsync(new RetractFactCommand(id, entry.Kind, entry.FactId), default);

        (await DeleteAsync(id)).Should().Be(DeleteMedicineOutcome.Deleted);
        (await _scope.Medicines.GetAsync(id, default)).Should().BeNull();
        _scope.Retractions.All.Should().BeEmpty();
    }

    [Fact]
    public async Task An_intake_a_suspension_or_a_legacy_row_blocks_the_deletion()
    {
        var withIntake = await SeedAsync();
        await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(withIntake, new DateOnly(2026, 9, 12), IntakeStatus.Taken, 1m), default);
        var withSuspension = await SeedAsync();
        await _scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(withSuspension, new DateOnly(2026, 9, 12)), default);
        var withLegacy = await SeedAsync(initialQuantity: 30m);
        _scope.FreezeLedger();

        (await DeleteAsync(withIntake)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);
        (await DeleteAsync(withSuspension)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);
        (await DeleteAsync(withLegacy)).Should().Be(DeleteMedicineOutcome.HasRecordedFacts);
        (await _scope.Medicines.ListAllAsync(default)).Should().HaveCount(3);
    }

    [Fact]
    public async Task An_unknown_medicine_is_reported_as_not_found()
    {
        (await DeleteAsync(Guid.NewGuid())).Should().Be(DeleteMedicineOutcome.NotFound);
        (await _scope.DeleteMedicine.CheckAsync(Guid.NewGuid(), default)).Should().Be(DeleteMedicineOutcome.NotFound);
    }

    [Fact]
    public async Task Without_sync_nothing_is_recorded()
    {
        var id = await SeedAsync();

        await DeleteAsync(id);

        _scope.SyncOperations.All.Should().BeEmpty();
    }

    [Fact]
    public async Task With_sync_the_deletion_is_one_operation_and_the_registers_go()
    {
        _scope.EnableSync();
        var id = await SeedAsync();
        _scope.SyncVersions.All.Should().Contain(v => v.MedicineId == id);
        var before = _scope.SyncOperations.All.Count;

        await DeleteAsync(id);

        var added = _scope.SyncOperations.All.Skip(before).Should().ContainSingle().Subject;
        added.Type.Should().Be("MedicineDeleted");
        added.SchemaVersion.Should().Be(2);
        added.MedicineId.Should().Be(id);
        OperationCodec.Deserialize(added.Type, added.SchemaVersion, added.Payload)
            .Should().Be(new MedicineDeleted(id, _scope.Clock.GetUtcNow()));
        _scope.SyncVersions.All.Should().NotContain(v => v.MedicineId == id);
        // The log keeps the medicine's operations: the deletion is its
        // tombstone for the other devices.
        _scope.SyncOperations.All.Where(o => o.MedicineId == id).Should().HaveCount(before + 1);
    }
}
