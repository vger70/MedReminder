using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Verifica end-to-end che i use case Application, cablati sui repository
// EF Core reali, producano lo stato atteso in un DB SQLite persistente.
// Exists to catch misalignments between the domain / application
// logic and the persistent mapping before they show up in
// production.
public class EndToEndUseCaseTests
{
    private static TestTime FixedClock => new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Add_medicine_then_add_stock_produces_two_movements_and_new_epoch()
    {
        using var fixture = new SqliteInMemoryFixture();

        Guid medicineId;
        await using (var ctx = fixture.CreateContext())
        {
            var addMedicine = new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                FixedClock);

            medicineId = await addMedicine.ExecuteAsync(new AddMedicineCommand(
                Name: "Enalapril",
                Unit: "compresse",
                DosePerAdministration: 1m,
                AdministrationsPerDay: 2,
                StartDate: new DateOnly(2026, 9, 1),
                ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows,
                InitialQuantity: 30m), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var addStock = new AddStock(
                new MedicineRepository(ctx),
                new StockMovementRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                FixedClock);
            await addStock.ExecuteAsync(
                new AddStockCommand(medicineId, 30m, StockMovementKind.NewPackage),
                CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var medicineRepo = new MedicineRepository(ctx);
            var stockRepo = new StockMovementRepository(ctx);

            var medicine = await medicineRepo.GetAsync(medicineId, CancellationToken.None);
            medicine!.StockEpoch.Should().Be(2);

            var movements = await stockRepo.ListForMedicineAsync(medicineId, CancellationToken.None);
            movements.Should().HaveCount(2);
            MedicineStock.Current(movements).Should().Be(60m);
        }
    }

    [Fact]
    public async Task Consumption_catch_up_and_monitor_agree_on_state()
    {
        using var fixture = new SqliteInMemoryFixture();
        var recordEmail = new RecordingEmailNotificationService();
        var recordWindows = new RecordingWindowsNotificationService();
        Guid medicineId;

        await using (var ctx = fixture.CreateContext())
        {
            var addMedicine = new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                FixedClock);
            medicineId = await addMedicine.ExecuteAsync(new AddMedicineCommand(
                Name: "Enalapril",
                Unit: "compresse",
                DosePerAdministration: 1m,
                AdministrationsPerDay: 2,
                StartDate: new DateOnly(2026, 9, 10),
                ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows,
                InitialQuantity: 20m), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var catchUp = new ConsumptionCatchUp(
                new MedicineRepository(ctx), LedgerFor(ctx, FixedClock), new UnitOfWork(ctx));
            var created = await catchUp.RunAsync(CancellationToken.None);
            // Materializza consumo dal 10 al 12 settembre incluso = 3 giorni.
            created.Should().Be(3);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var stockRepo = new StockMovementRepository(ctx);
            var movements = await stockRepo.ListForMedicineAsync(medicineId, CancellationToken.None);
            MedicineStock.Current(movements).Should().Be(20m - 6m);   // 14 compresse residue
        }

        await using (var ctx = fixture.CreateContext())
        {
            var monitor = new MedicationMonitor(
                new MedicineRepository(ctx),
                new StockMovementRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationSuspensionRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new NotificationEventRepository(ctx),
                recordEmail,
                recordWindows,
                new UnitOfWork(ctx),
                FixedClock,
                NullLogger<MedicationMonitor>.Instance);

            var result = await monitor.RunAsync(CancellationToken.None);
            // 14 compresse, 2/giorno = 7 giorni residui = soglia -> notify.
            result.NotificationsSent.Should().Be(1);
            recordWindows.Sent.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Backdated_intake_replaces_the_automatic_day_with_a_non_utc_local_zone()
    {
        // SQLite returns movements with a zero offset; the catch-up and
        // RegisterIntake must map them back to the local day. At +13:00
        // local midday is 23:00 UTC of the previous day, so using the
        // UTC date would misplace every movement.
        using var fixture = new SqliteInMemoryFixture();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+13", TimeSpan.FromHours(13), "Test+13", "Test+13");
        var clock = new ZonedTestTime(new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero), zone);
        Guid medicineId;

        await using (var ctx = fixture.CreateContext())
        {
            medicineId = await new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx, clock),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                clock).ExecuteAsync(new AddMedicineCommand(
                    Name: "Enalapril",
                    Unit: "compresse",
                    DosePerAdministration: 1m,
                    AdministrationsPerDay: 2,
                    StartDate: new DateOnly(2026, 9, 10),
                    ThresholdDays: 7,
                    NotificationChannels: NotificationChannels.Windows,
                    InitialQuantity: 20m), CancellationToken.None);
        }

        ConsumptionCatchUp BuildCatchUp(MedReminderDbContext ctx) => new(
            new MedicineRepository(ctx), LedgerFor(ctx, clock), new UnitOfWork(ctx));

        await using (var ctx = fixture.CreateContext())
        {
            // Local today is 13/9: days 10, 11 and 12.
            (await BuildCatchUp(ctx).RunAsync(CancellationToken.None)).Should().Be(3);
        }

        await using (var ctx = fixture.CreateContext())
        {
            await new RegisterIntake(
                new MedicineRepository(ctx),
                new MedicationIntakeRepository(ctx),
                LedgerFor(ctx, clock),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                clock).ExecuteAsync(
                    new RegisterIntakeCommand(medicineId, new DateOnly(2026, 9, 11), IntakeStatus.Taken, 1m),
                    CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await BuildCatchUp(ctx).RunAsync(CancellationToken.None)).Should().Be(0);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var movements = await new StockMovementRepository(ctx, clock)
                .ListForMedicineAsync(medicineId, CancellationToken.None);
            // Day 11 (local) is derived: its automatic row is replaced
            // by the intake (Phase 2c-2), whose row sits on local day 11.
            movements.Should().NotContain(m => m.Kind == StockMovementKind.PositiveCorrection);
            movements.Where(m => m.Kind == StockMovementKind.Consumption
                    && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.OccurredAt, zone).DateTime) == new DateOnly(2026, 9, 11))
                .Should().ContainSingle().Which.QuantityDelta.Should().Be(-1m);
            MedicineStock.Current(movements).Should().Be(20m - 2m - 1m - 2m);
        }
    }

    // B.1 Phase 2c-2 on real SQLite: a retroactive schedule change
    // rewrites derived rows in place (same deterministic ids, EF update
    // of detached rows), and a stock count is stored as a fact whose
    // correction survives later ticks.
    [Fact]
    public async Task Derived_rows_follow_a_retroactive_change_and_a_count()
    {
        using var fixture = new SqliteInMemoryFixture();
        var clock = new TestTime(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero));
        Guid medicineId;

        await using (var ctx = fixture.CreateContext())
        {
            medicineId = await new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx, clock),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                clock).ExecuteAsync(new AddMedicineCommand(
                    Name: "Enalapril",
                    Unit: "compresse",
                    DosePerAdministration: 1m,
                    AdministrationsPerDay: 1,
                    StartDate: new DateOnly(2026, 9, 3),
                    ThresholdDays: 7,
                    NotificationChannels: NotificationChannels.Windows,
                    InitialQuantity: 50m), CancellationToken.None);
        }

        async Task<int> TickAsync()
        {
            await using var ctx = fixture.CreateContext();
            return await new ConsumptionCatchUp(
                new MedicineRepository(ctx), LedgerFor(ctx, clock), new UnitOfWork(ctx)).RunAsync(CancellationToken.None);
        }

        async Task<IReadOnlyList<StockMovement>> LedgerAsync()
        {
            await using var ctx = fixture.CreateContext();
            return await new StockMovementRepository(ctx, clock).ListForMedicineAsync(medicineId, CancellationToken.None);
        }

        (await TickAsync()).Should().Be(10);                      // 3..12 September at 1/day
        var idsBefore = (await LedgerAsync()).Select(m => m.Id).ToHashSet();

        await using (var ctx = fixture.CreateContext())
        {
            await new ChangeMedicationSchedule(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                clock).ExecuteAsync(
                    new ChangeMedicationScheduleCommand(medicineId, 1m, 2, new DateOnly(2026, 9, 8)),
                    CancellationToken.None);
        }

        (await TickAsync()).Should().Be(0, "days 8..12 keep their ids and are updated in place");
        var afterChange = await LedgerAsync();
        afterChange.Select(m => m.Id).Should().BeEquivalentTo(idsBefore);
        MedicineStock.Current(afterChange).Should().Be(50m - 5m - 10m);   // D6

        await using (var ctx = fixture.CreateContext())
        {
            var reconcile = new ReconcileStock(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationSuspensionRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockCountRepository(ctx),
                LedgerFor(ctx, clock),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                clock);
            var result = await reconcile.ExecuteAsync(
                new ReconcileStockCommand(medicineId, 30m, TakenToday: 0m, Notes: "count"), CancellationToken.None);
            result.Correction.Should().Be(-5m);
        }

        (await TickAsync()).Should().Be(0);
        var afterCount = await LedgerAsync();
        MedicineStock.Current(afterCount).Should().Be(30m);
        afterCount.Should().ContainSingle(m => m.Kind == StockMovementKind.NegativeCorrection)
            .Which.Notes.Should().Be("count");
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.StockCounts.SingleAsync()).Correction.Should().Be(-5m);
        }
    }

    private static LedgerSynchronizer LedgerFor(MedReminderDbContext ctx, TimeProvider clock)
    {
        var stock = new StockMovementRepository(ctx, clock);
        return new LedgerSynchronizer(
            new LedgerFactsLoader(
                stock,
                new MedicationIntakeRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationSuspensionRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new MedicineActivityRepository(ctx),
                new StockCountRepository(ctx),
                new LedgerCutoffRepository(ctx)),
            stock,
            new MedicineRepository(ctx),
            new NotificationEventRepository(ctx),
            clock);
    }

    // Deterministic clock for the integration tests (LocalTimeZone
    // forced to UTC as in the FakeTimeProvider of Application.Tests).
    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ZonedTestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        private readonly TimeZoneInfo _zone;
        public ZonedTestTime(DateTimeOffset now, TimeZoneInfo zone)
        {
            _now = now.ToUniversalTime();
            _zone = zone;
        }
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => _zone;
    }

    // Minimal fakes for notification outputs: we do NOT resurrect
    // the InMemory ones from Application.Tests to avoid a
    // cross-reference between two test projects.
    private sealed class RecordingEmailNotificationService
        : MedReminder.Application.Abstractions.IEmailNotificationService
    {
        public List<MedReminder.Application.Notifications.EmailMessage> Sent { get; } = new();
        public Task SendAsync(
            MedReminder.Application.Notifications.EmailMessage message,
            CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private sealed class RecordingWindowsNotificationService
        : MedReminder.Application.Abstractions.IWindowsNotificationService
    {
        public List<(string Title, string Body)> Sent { get; } = new();
        public Task ShowAsync(string title, string body, CancellationToken cancellationToken)
        {
            Sent.Add((title, body));
            return Task.CompletedTask;
        }
    }
}
