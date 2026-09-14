using FluentAssertions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Verifica end-to-end che i use case Application, cablati sui repository
// EF Core reali, producano lo stato atteso in un DB SQLite persistente.
// Serve a scoprire disallineamenti fra la logica di dominio/applicativa
// e la mappatura persistente prima che si manifestino in produzione.
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
                new StockMovementRepository(ctx),
                new UnitOfWork(ctx),
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
                new UnitOfWork(ctx),
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
                new StockMovementRepository(ctx),
                new UnitOfWork(ctx),
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
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationSuspensionRepository(ctx),
                new StockMovementRepository(ctx),
                new UnitOfWork(ctx),
                FixedClock);
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

    // Cronometro deterministico per i test integrazione (LocalTimeZone
    // forzato a UTC come nel FakeTimeProvider degli Application.Tests).
    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    // Fakes minimi per gli output notifiche: NON riesumiamo gli InMemory
    // da Application.Tests per non introdurre una cross-reference tra
    // due progetti test.
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
