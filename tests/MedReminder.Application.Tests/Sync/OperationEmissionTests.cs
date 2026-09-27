using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// B.1 Phase 3a: every use case emits the facts it writes, with the ids
// of the stored rows, and nothing derived.
public class OperationEmissionTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);

    private readonly ApplicationTestScope _scope = new();

    private List<SyncOperationBody> Emitted()
        => [.. _scope.SyncOperations.All
            .OrderBy(o => o.Timestamp)
            .Select(o => OperationCodec.Deserialize(o.Type, o.SchemaVersion, o.Payload))];

    private async Task<Guid> SeedAsync(IReadOnlyList<AdministrationSlotInput>? slots = null)
        => await _scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows,
                Notes: "with food", InitialQuantity: 30m, AdministrationSlots: slots),
            CancellationToken.None);

    private UpdateMedicineCommand Update(Guid id, bool isActive = true, string? notes = "with food")
        => new(id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows,
            null, null, notes, isActive);

    [Fact]
    public async Task Disabled_sync_records_nothing()
    {
        var id = await SeedAsync();
        await _scope.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);

        _scope.SyncOperations.All.Should().BeEmpty();
    }

    [Fact]
    public async Task AddMedicine_emits_creation_schedule_initial_stock_and_slots()
    {
        _scope.EnableSync();

        var id = await SeedAsync([new AdministrationSlotInput(1m, new TimeOnly(8, 0), " morning ")]);

        var ops = Emitted();
        ops.Select(o => o.GetType()).Should().Equal(
            typeof(MedicineCreated), typeof(ScheduleRowRecorded), typeof(StockEntryRecorded), typeof(SlotSetRecorded));
        ops.Should().AllSatisfy(o => o.MedicineId.Should().Be(id));

        var created = (MedicineCreated)ops[0];
        created.StartDate.Should().Be(Start);
        created.Fields.Should().Contain(new MedicineFieldValue("Notes", "with food"));
        created.Fields.Select(f => f.Field).Should().Equal(MedicineFieldCodec.FieldNames);

        var row = (await _scope.Schedules.ListForMedicineAsync(id, default)).Single();
        ((ScheduleRowRecorded)ops[1]).RowId.Should().Be(row.Id);

        var entry = (StockEntryRecorded)ops[2];
        entry.Kind.Should().Be(StockMovementKind.InitialLoad);
        entry.QuantityDelta.Should().Be(30m);
        _scope.Stock.All.Should().Contain(m => m.Id == entry.MovementId);

        var set = (SlotSetRecorded)ops[3];
        set.Slots.Should().ContainSingle().Which.TimingLabel.Should().Be("morning");
        (await _scope.Slots.ListForMedicineAsync(id, default)).Single().Id.Should().Be(set.Slots[0].SlotId);
    }

    [Fact]
    public async Task Stock_intake_count_and_retraction_emit_their_facts_only()
    {
        var id = await SeedAsync();
        _scope.EnableSync();

        await _scope.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        await _scope.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, 2m, "broken"), default);
        var intakeId = await _scope.RegisterIntake.ExecuteAsync(
            new RegisterIntakeCommand(id, new DateOnly(2026, 9, 12), IntakeStatus.Taken, 1m), default);
        await _scope.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 40m, 0m), default);
        var count = (await _scope.Counts.ListForMedicineAsync(id, default)).Single();
        await _scope.RetractFact.ExecuteAsync(new RetractFactCommand(id, FactKind.StockCount, count.Id), default);

        var ops = Emitted();
        ops.Select(o => o.GetType()).Should().Equal(
            typeof(StockEntryRecorded), typeof(StockEntryRecorded), typeof(IntakeRecorded),
            typeof(StockCountRecorded), typeof(FactRetracted));
        ((StockEntryRecorded)ops[0]).Kind.Should().Be(StockMovementKind.NewPackage);
        ((StockEntryRecorded)ops[1]).QuantityDelta.Should().Be(-2m);
        ((StockEntryRecorded)ops[1]).Notes.Should().Be("broken");
        ((IntakeRecorded)ops[2]).IntakeId.Should().Be(intakeId);

        var counted = (StockCountRecorded)ops[3];
        counted.CountId.Should().Be(count.Id);
        counted.Correction.Should().Be(count.Correction);
        counted.LedgerAtStartOfDay.Should().Be(count.LedgerAtStartOfDay);

        var retracted = (FactRetracted)ops[4];
        retracted.FactId.Should().Be(count.Id);
        retracted.Kind.Should().Be(FactKind.StockCount);
        retracted.RetractionId.Should().Be(_scope.Retractions.All.Single().Id);
    }

    [Fact]
    public async Task Derived_rows_written_by_the_catch_up_are_not_operations()
    {
        var id = await SeedAsync();
        _scope.EnableSync();
        _scope.Clock.AdvanceBy(TimeSpan.FromDays(5));

        var created = await _scope.ConsumptionCatchUp.RunAsync(default);

        created.Should().BeGreaterThan(0);
        _scope.SyncOperations.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_suspension_and_activity_changes_emit_their_facts()
    {
        var id = await SeedAsync();
        _scope.EnableSync();

        await _scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 1, new DateOnly(2026, 9, 20)), default);
        var suspensionId = await _scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 21), "trip"), default);
        await _scope.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, new DateOnly(2026, 9, 25)), default);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default);
        await _scope.DeactivateMedicine.ExecuteAsync(new DeactivateMedicineCommand(id), default);

        var ops = Emitted();
        ops.Select(o => o.GetType()).Should().Equal(
            typeof(ScheduleRowRecorded), typeof(SuspensionRecorded), typeof(SuspensionEndChanged),
            typeof(MedicineActivityChanged));
        ((ScheduleRowRecorded)ops[0]).DosePerAdministration.Should().Be(2m);
        ((SuspensionRecorded)ops[1]).SuspensionId.Should().Be(suspensionId);
        ((SuspensionEndChanged)ops[2]).Should().Be(new SuspensionEndChanged(id, suspensionId, new DateOnly(2026, 9, 25)));
        ((MedicineActivityChanged)ops[3]).Active.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMedicine_emits_only_the_fields_that_change()
    {
        var id = await SeedAsync();
        _scope.EnableSync();

        await _scope.UpdateMedicine.ExecuteAsync(Update(id, notes: "  after meals "), default);
        await _scope.UpdateMedicine.ExecuteAsync(Update(id, isActive: false, notes: "after meals"), default);

        var ops = Emitted();
        ops.Should().HaveCount(2);
        ops[0].Should().Be(new MedicineFieldChanged(id, "Notes", "after meals"));
        ops[1].Should().BeOfType<MedicineActivityChanged>().Which.Active.Should().BeFalse();
    }
}
