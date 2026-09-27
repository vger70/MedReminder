using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// B.1 Phase 3b-2: stock counts evaluated again on the facts recorded
// before them by HLC (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.2,
// §4.3 rule 3).
public class CountReevaluationTests
{
    private static readonly DateOnly Start = new(2026, 9, 1);
    private static readonly DateOnly Today = new(2026, 9, 13);

    private readonly ApplicationTestScope _desktop = new(new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _phone = new(new DateTimeOffset(2026, 9, 13, 10, 0, 0, 300, TimeSpan.Zero));

    private async Task<Guid> SeedAsync()
    {
        var group = _desktop.EnableSync(Guid.Parse("0d000000-0000-0000-0000-000000000000"));
        _phone.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0e000000-0000-0000-0000-000000000000") });
        var id = await _desktop.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows, InitialQuantity: 60m), default);
        await PullAsync(_desktop, _phone);
        return id;
    }

    private static async Task PullAsync(ApplicationTestScope from, ApplicationTestScope to)
        => await to.ApplyRemote.ExecuteAsync(await from.SyncOperations.ListAllAsync(default), default);

    private static async Task ExchangeAsync(ApplicationTestScope a, ApplicationTestScope b)
    {
        await PullAsync(a, b);
        await PullAsync(b, a);
    }

    private static async Task<decimal> StockAsync(ApplicationTestScope scope, Guid id)
        => (await scope.Ledger.SynchronizeAsync((await scope.Medicines.GetAsync(id, default))!, default)).Ledger.Stock;

    [Fact]
    public async Task A_refill_recorded_before_a_count_on_another_device_is_counted_once()
    {
        var id = await SeedAsync();
        await _desktop.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        // The phone has not seen the refill; the pills were in the box.
        _phone.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _phone.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 40m, 0m), default);

        await ExchangeAsync(_desktop, _phone);

        (await StockAsync(_desktop, id)).Should().Be(40m);
        (await StockAsync(_phone, id)).Should().Be(40m);
    }

    [Fact]
    public async Task A_fact_retracted_on_one_device_leaves_the_concurrent_count_of_the_other()
    {
        // The product owner's case (§4.2): the desktop retracts a refill of
        // 28, the phone concurrently counts 40 with the refill included.
        var id = await SeedAsync();
        await _desktop.AddStock.ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), default);
        await PullAsync(_desktop, _phone);
        var refill = _desktop.Stock.All.Single(m => m.Kind == StockMovementKind.NewPackage).Id;
        await _desktop.RetractFact.ExecuteAsync(new RetractFactCommand(id, FactKind.StockEntry, refill), default);
        _phone.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _phone.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 40m, 0m), default);
        (await StockAsync(_phone, id)).Should().Be(40m);

        await ExchangeAsync(_desktop, _phone);

        (await StockAsync(_phone, id)).Should().Be(40m, "the retracted refill is out of the count's snapshot too");
        (await StockAsync(_desktop, id)).Should().Be(40m);
    }

    [Fact]
    public async Task Taken_today_is_capped_by_what_the_merged_facts_leave_due()
    {
        var id = await SeedAsync();
        await _desktop.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, Today, IntakeStatus.Taken, 1m), default);
        _phone.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _phone.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 40m, 2m), default);

        await ExchangeAsync(_desktop, _phone);

        (await StockAsync(_desktop, id)).Should().Be(await StockAsync(_phone, id));
        var count = (await _desktop.Ledger.LoadFactsAsync((await _desktop.Medicines.GetAsync(id, default))!, default)).Counts.Single();
        count.TakenToday.Should().Be(2m, "the stored input is kept");
        count.CountDayScheduled.Should().Be(0m);
    }

    [Fact]
    public async Task Registers_are_read_as_of_the_count_with_genesis_versions()
    {
        // Medicine written before sync, then genesis versions, then a count
        // and a later change of the therapy end date.
        var id = await _desktop.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows, InitialQuantity: 60m), default);
        _desktop.EnableSync();
        (await _desktop.Genesis.RecordAsync(default)).Should().BeGreaterThan(0);
        await _desktop.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, 40m, 0m), default);
        var stored = (await _desktop.Counts.ListForMedicineAsync(id, default)).Single();

        _desktop.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _desktop.UpdateMedicine.ExecuteAsync(new UpdateMedicineCommand(
            id, "Enalapril", null, null, "compresse", 7, NotificationChannels.Windows,
            Today.AddDays(-3), null, null, true), default);

        var medicine = await _desktop.Medicines.GetAsync(id, default);
        var count = (await _desktop.Ledger.LoadFactsAsync(medicine!, default)).Counts.Single();
        count.Correction.Should().Be(stored.Correction);
        count.LedgerAtStartOfDay.Should().Be(stored.LedgerAtStartOfDay);
    }

    [Fact]
    public async Task Genesis_versions_are_written_once()
    {
        await _desktop.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows), default);

        var first = await _desktop.Genesis.RecordAsync(default);
        var second = await _desktop.Genesis.RecordAsync(default);

        first.Should().Be(Domain.Sync.MedicineFieldCodec.FieldNames.Count + 1);
        second.Should().Be(0);
        _desktop.SyncVersions.All.Should().OnlyContain(v => v.Version == SyncGenesis.Timestamp);
    }

    // On one device nothing is recorded behind a count's back, so the
    // re-evaluated outcome must be the stored one (parity).
    [Fact]
    public async Task On_one_device_reevaluation_matches_the_stored_outcomes()
    {
        var checkedCounts = 0;
        for (var seed = 1; seed <= 150; seed++)
        {
            var scope = new ApplicationTestScope(new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero));
            var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 2, Start, 7, NotificationChannels.Windows, InitialQuantity: 60m,
                AdministrationSlots: [new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                    new AdministrationSlotInput(1m, new TimeOnly(20, 0), null)]), default);
            scope.EnableSync();
            await scope.Genesis.RecordAsync(default);
            var random = new Random(seed);

            for (var step = 0; step < 40; step++)
            {
                var today = DateOnly.FromDateTime(scope.Clock.GetUtcNow().UtcDateTime);
                try
                {
                    switch (random.Next(8))
                    {
                        case 0: await scope.AddStock.ExecuteAsync(new AddStockCommand(id, random.Next(1, 30), StockMovementKind.NewPackage), default); break;
                        case 1: await scope.RegisterIntake.ExecuteAsync(new RegisterIntakeCommand(id, today.AddDays(-random.Next(0, 3)), IntakeStatus.Taken, 1m), default); break;
                        case 2: await scope.ReconcileStock.ExecuteAsync(new ReconcileStockCommand(id, random.Next(0, 80), random.Next(0, 3)), default); break;
                        case 3: await scope.SuspendMedication.ExecuteAsync(new SuspendMedicationCommand(id, today.AddDays(random.Next(-2, 2))), default); break;
                        case 4: await scope.ResumeMedication.ExecuteAsync(new ResumeMedicationCommand(id, today.AddDays(random.Next(0, 2))), default); break;
                        case 5: await scope.ChangeMedicationSchedule.ExecuteAsync(new ChangeMedicationScheduleCommand(id, random.Next(1, 3), random.Next(1, 3), today), default); break;
                        case 6: await scope.UpdateMedicine.ExecuteAsync(new UpdateMedicineCommand(id, "Enalapril", null, null, "compresse", 7,
                            NotificationChannels.Windows, random.Next(2) == 0 ? null : today.AddDays(random.Next(-3, 5)), null, null, true), default); break;
                        default: scope.Clock.AdvanceBy(TimeSpan.FromHours(random.Next(1, 30))); break;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                }
                scope.Clock.AdvanceBy(TimeSpan.FromMinutes(random.Next(1, 60)));
            }

            var facts = await scope.Ledger.LoadFactsAsync((await scope.Medicines.GetAsync(id, default))!, default);
            var stored = (await scope.Counts.ListForMedicineAsync(id, default)).ToDictionary(c => c.Id);
            foreach (var count in facts.Counts)
            {
                var original = stored[count.Id];
                count.Correction.Should().Be(original.Correction, $"seed {seed}");
                count.LedgerAtStartOfDay.Should().Be(original.LedgerAtStartOfDay, $"seed {seed}");
                count.CountDayScheduled.Should().Be(original.CountDayScheduled, $"seed {seed}");
                count.MaterializesCountDay.Should().Be(original.MaterializesCountDay, $"seed {seed}");
                count.AdvancesEpoch.Should().Be(original.AdvancesEpoch, $"seed {seed}");
                checkedCounts++;
            }
        }
        checkedCounts.Should().BeGreaterThan(100);
    }
}
