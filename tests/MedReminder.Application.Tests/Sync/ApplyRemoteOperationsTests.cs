using FluentAssertions;
using MedReminder.Application.Abstractions;
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

// B.1 Phase 3b-1: merge of operations between two devices, on the
// in-memory repositories. The SQLite path and random workloads are in
// SyncConvergenceTests (Infrastructure.Portable.Tests).
public class ApplyRemoteOperationsTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);

    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, 500, TimeSpan.Zero));

    public ApplyRemoteOperationsTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
    }

    private static async Task<ApplyRemoteResult> PullAsync(ApplicationTestScope from, ApplicationTestScope to)
        => await to.ApplyRemote.ExecuteAsync(
            await from.SyncOperations.ListAllAsync(default), CancellationToken.None);

    private async Task<Guid> SeedOnAAsync()
    {
        var id = await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows,
            Notes: "with food", InitialQuantity: 30m,
            AdministrationSlots: [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)]), default);
        await PullAsync(_a, _b);
        return id;
    }

    private static UpdateMedicineCommand Edit(Guid id, string? notes, string? doctor = null) => new(
        id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows, null, doctor, notes, true);

    private static async Task<decimal> StockAsync(ApplicationTestScope scope, Guid id)
    {
        var medicine = await scope.Medicines.GetAsync(id, default);
        return (await scope.Ledger.SynchronizeAsync(medicine!, default)).Ledger.Stock;
    }

    [Fact]
    public async Task A_medicine_created_on_one_device_appears_on_the_other()
    {
        var id = await SeedOnAAsync();

        var copy = await _b.Medicines.GetAsync(id, default);
        var original = await _a.Medicines.GetAsync(id, default);
        MedicineFieldCodec.Snapshot(copy!).Should().Equal(MedicineFieldCodec.Snapshot(original!));
        copy!.StartDate.Should().Be(Start);
        copy.DosePerAdministration.Should().Be(1m);
        copy.AdministrationsPerDay.Should().Be(2);
        (await _b.Slots.ListForMedicineAsync(id, default)).Select(s => s.Id)
            .Should().Equal((await _a.Slots.ListForMedicineAsync(id, default)).Select(s => s.Id));
        (await StockAsync(_b, id)).Should().Be(await StockAsync(_a, id));
    }

    [Fact]
    public async Task Stock_facts_from_both_devices_are_merged()
    {
        var id = await SeedOnAAsync();
        await _a.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        await _b.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, new DateOnly(2026, 9, 12), IntakeStatus.Taken, 2m), default);
        await _b.AdjustStockDown.ExecuteAsync(new AdjustStockDownCommand(id, 3m), default);

        await PullAsync(_a, _b);
        await PullAsync(_b, _a);

        (await StockAsync(_a, id)).Should().Be(await StockAsync(_b, id));
        (await _a.Intakes.ListForMedicineAsync(id, default)).Should().ContainSingle();
        (await _b.Medicines.GetAsync(id, default))!.StockEpoch.Should().Be((await _a.Medicines.GetAsync(id, default))!.StockEpoch);
    }

    [Fact]
    public async Task Redelivery_and_echoes_are_skipped()
    {
        var id = await SeedOnAAsync();

        var again = await PullAsync(_a, _b);
        var echo = await PullAsync(_b, _a);

        again.Applied.Should().Be(0);
        echo.Applied.Should().Be(0);
        (await _b.Stock.ListForMedicineAsync(id, default)).Count(m => m.Origin == StockMovementOrigin.User).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_edits_of_a_field_converge_on_the_later_one_and_are_listed_on_both()
    {
        var id = await SeedOnAAsync();
        await _a.UpdateMedicine.ExecuteAsync(Edit(id, "from A"), default);
        await _b.UpdateMedicine.ExecuteAsync(Edit(id, "from B"), default); // later clock

        await PullAsync(_a, _b);
        await PullAsync(_b, _a);

        (await _a.Medicines.GetAsync(id, default))!.Notes.Should().Be("from B");
        (await _b.Medicines.GetAsync(id, default))!.Notes.Should().Be("from B");
        var onA = _a.SyncConflicts.All.Should().ContainSingle().Subject;
        _b.SyncConflicts.All.Should().ContainSingle().Which.Id.Should().Be(onA.Id);
        onA.Kind.Should().Be(SyncConflictKind.MedicineField);
        onA.Register.Should().Be("Notes");
        onA.LosingValue.Should().Be("from A");
        onA.WinningValue.Should().Be("from B");
    }

    [Fact]
    public async Task An_edit_made_after_receiving_the_other_is_not_a_conflict()
    {
        var id = await SeedOnAAsync();
        await _a.UpdateMedicine.ExecuteAsync(Edit(id, "from A"), default);
        await PullAsync(_a, _b);
        await _b.UpdateMedicine.ExecuteAsync(Edit(id, "from B"), default);
        await PullAsync(_b, _a);

        (await _a.Medicines.GetAsync(id, default))!.Notes.Should().Be("from B");
        _a.SyncConflicts.All.Should().BeEmpty();
        _b.SyncConflicts.All.Should().BeEmpty();
    }

    [Fact]
    public async Task An_older_remote_write_loses_and_leaves_the_local_value()
    {
        var id = await SeedOnAAsync();
        await _b.UpdateMedicine.ExecuteAsync(Edit(id, "from B", doctor: "Rossi"), default);
        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(5));
        await _a.UpdateMedicine.ExecuteAsync(Edit(id, "from A"), default);

        await PullAsync(_b, _a);

        var onA = await _a.Medicines.GetAsync(id, default);
        onA!.Notes.Should().Be("from A");
        onA.DoctorName.Should().Be("Rossi"); // not written by A: B's value applies
    }

    [Fact]
    public async Task A_retraction_wins_over_its_fact_in_either_order()
    {
        var id = await SeedOnAAsync();
        await _a.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        var refill = _a.Stock.All.Single(m => m.Kind == StockMovementKind.NewPackage).Id;
        await _a.RetractFact.ExecuteAsync(new RetractFactCommand(id, FactKind.StockEntry, refill), default);

        // Out of causal order on purpose: the tombstone first.
        var log = await _a.SyncOperations.ListAllAsync(default);
        var retraction = log.Single(o => o.Type == "FactRetracted");
        await _b.ApplyRemote.ExecuteAsync([retraction], default);
        await _b.ApplyRemote.ExecuteAsync(log, default);

        _b.Stock.All.Should().NotContain(m => m.Id == refill);
        _b.Retractions.All.Should().ContainSingle(r => r.FactId == refill);
        (await StockAsync(_b, id)).Should().Be(await StockAsync(_a, id));
    }

    [Fact]
    public async Task Concurrent_suspension_end_dates_converge_and_overlaps_are_hinted()
    {
        var id = await SeedOnAAsync();
        var suspension = await _a.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, new DateOnly(2026, 9, 5)), default);
        await PullAsync(_a, _b);
        await _a.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, new DateOnly(2026, 9, 8)), default);
        await _b.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, new DateOnly(2026, 9, 9)), default);
        await _b.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, new DateOnly(2026, 9, 9)), default);

        await PullAsync(_a, _b);
        await PullAsync(_b, _a);

        var endA = (await _a.Suspensions.ListForMedicineAsync(id, default)).Single(s => s.Id == suspension).EndDate;
        var endB = (await _b.Suspensions.ListForMedicineAsync(id, default)).Single(s => s.Id == suspension).EndDate;
        endA.Should().Be(new DateOnly(2026, 9, 9)).And.Be(endB);
        _a.SyncConflicts.All.Should().ContainSingle(c => c.Kind == SyncConflictKind.OverlappingSuspensions);
        (await StockAsync(_a, id)).Should().Be(await StockAsync(_b, id));
    }

    [Fact]
    public async Task Intakes_of_one_day_from_two_devices_over_the_schedule_are_hinted()
    {
        var id = await SeedOnAAsync();
        var day = new DateOnly(2026, 9, 12);
        await _a.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, day, IntakeStatus.Taken, 2m), default);
        await _b.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, day, IntakeStatus.Taken, 1m), default);

        await PullAsync(_b, _a);

        _a.SyncConflicts.All.Should().ContainSingle(c => c.Kind == SyncConflictKind.IntakesOverSchedule);
        (await _a.Intakes.ListForMedicineAsync(id, default)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_slot_sets_and_same_date_schedules_are_listed_and_converge()
    {
        var id = await SeedOnAAsync();
        var day = new DateOnly(2026, 9, 20);
        await _a.ChangeMedicationSchedule.ExecuteAsync(new ChangeMedicationScheduleCommand(id, 2m, 1, day), default);
        await _b.ChangeMedicationSchedule.ExecuteAsync(new ChangeMedicationScheduleCommand(id, 3m, 1, day), default);
        _a.Clock.AdvanceBy(TimeSpan.FromSeconds(1));
        await _a.UpdateMedicine.ExecuteAsync(Edit(id, "with food") with
        {
            AdministrationSlots = [new AdministrationSlotInput(1m, new TimeOnly(9, 0), null)],
        }, default);
        _b.Clock.AdvanceBy(TimeSpan.FromSeconds(2));
        await _b.UpdateMedicine.ExecuteAsync(Edit(id, "with food") with
        {
            AdministrationSlots = [new AdministrationSlotInput(2m, new TimeOnly(9, 0), null)],
        }, default);

        await PullAsync(_a, _b);
        await PullAsync(_b, _a);

        foreach (var scope in new[] { _a, _b })
        {
            var medicine = await scope.Medicines.GetAsync(id, default);
            medicine!.DosePerAdministration.Should().Be(3m);
            scope.SyncConflicts.All.Select(c => c.Kind).Should().BeEquivalentTo(
                [SyncConflictKind.ScheduleSameDate, SyncConflictKind.SlotSetReplaced]);
        }
        (await _a.Slots.ListForMedicineAsync(id, default)).Single().Dose
            .Should().Be((await _b.Slots.ListForMedicineAsync(id, default)).Single().Dose);
        (await StockAsync(_a, id)).Should().Be(await StockAsync(_b, id));
    }

    [Fact]
    public async Task An_unknown_operation_stops_the_batch_after_what_came_before()
    {
        var id = await SeedOnAAsync();
        await _a.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        var log = (await _a.SyncOperations.ListAllAsync(default)).ToList();
        var refill = log[^1];
        var future = new SyncOperation
        {
            HlcPhysicalMs = refill.HlcPhysicalMs, HlcCounter = refill.HlcCounter + 1, DeviceId = refill.DeviceId,
            Generation = 1, Type = "DeviceRevoked", SchemaVersion = 1, MedicineId = id, Payload = "{}",
        };

        var result = await _b.ApplyRemote.ExecuteAsync([refill, future], default);

        result.Applied.Should().Be(1);
        result.Blocked.Should().BeSameAs(future);
        _b.Stock.All.Should().Contain(m => m.Id == _a.Stock.All.Single(x => x.Kind == StockMovementKind.NewPackage).Id);
    }

    [Fact]
    public async Task An_operation_for_an_unknown_medicine_is_a_causal_order_error()
    {
        await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 1, Start, 7, NotificationChannels.Windows, InitialQuantity: 30m), default);
        var withoutCreation = (await _a.SyncOperations.ListAllAsync(default)).Skip(1).ToList();

        await FluentActions.Awaiting(() => _b.ApplyRemote.ExecuteAsync(withoutCreation, default))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*causal order*");
    }

    [Fact]
    public async Task Applying_requires_sync_to_be_enabled()
    {
        var scope = new ApplicationTestScope();

        await FluentActions.Awaiting(() => scope.ApplyRemote.ExecuteAsync([], default))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
