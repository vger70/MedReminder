using FluentAssertions;
using MedReminder.Application.Ledger;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Ledger;
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

// B.1 Phase 2d on real SQLite: retraction removes detached rows through
// the unit of work, records the tombstone, and the epoch fact id is kept
// and backfilled on older notification events. Plus the 2d boot patch.
public class FactRetractionPersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Retractions_of_every_kind_persist()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SeedAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            await new AddStock(new MedicineRepository(ctx), new StockMovementRepository(ctx, Clock), TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock)
                .ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new RegisterIntake(new MedicineRepository(ctx), new MedicationIntakeRepository(ctx), LedgerFor(ctx),
                    TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock)
                .ExecuteAsync(new RegisterIntakeCommand(id, new DateOnly(2026, 9, 10), IntakeStatus.Taken, 3m),
                    CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new SuspendMedication(new MedicineRepository(ctx), new MedicationSuspensionRepository(ctx),
                    TestOperationLog.For(ctx), new UnitOfWork(ctx), Clock)
                .ExecuteAsync(new SuspendMedicationCommand(id, new DateOnly(2026, 9, 20)), CancellationToken.None);
        }

        foreach (var kind in new[] { FactKind.Suspension, FactKind.Intake, FactKind.StockEntry })
        {
            await using var ctx = fixture.CreateContext();
            var history = History(ctx);
            var item = (await history.LoadAsync(id, CancellationToken.None))
                .First(i => i.Kind == kind && i.MovementKind != StockMovementKind.InitialLoad);
            await Retract(ctx, history).ExecuteAsync(new RetractFactCommand(id, kind, item.FactId), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.FactRetractions.CountAsync()).Should().Be(3);
            (await ctx.MedicationIntakes.AnyAsync()).Should().BeFalse();
            (await ctx.MedicationSuspensions.AnyAsync()).Should().BeFalse();
            var movements = await new StockMovementRepository(ctx, Clock).ListForMedicineAsync(id, CancellationToken.None);
            movements.Should().NotContain(m => m.Kind == StockMovementKind.NewPackage);
            MedicineStock.Current(movements).Should().Be(40m);
            var medicine = await ctx.Medicines.SingleAsync();
            medicine.StockEpoch.Should().Be(1);
            medicine.StockEpochFactId.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Older_notification_events_are_backfilled_in_the_unit_of_work()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            ctx.NotificationEvents.Add(new NotificationEvent
            {
                MedicineId = id, StockEpoch = 1, TriggeredAt = Clock.GetUtcNow(),
                Channel = NotificationChannels.Windows, DaysRemainingAtSend = 5, Success = true,
            });
            var medicine = await ctx.Medicines.SingleAsync();
            medicine.StockEpochFactId = null;
            await ctx.SaveChangesAsync();
        }

        await TickAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            var medicine = await ctx.Medicines.SingleAsync();
            (await ctx.NotificationEvents.SingleAsync()).EpochFactId.Should().Be(medicine.StockEpochFactId!.Value);
        }
    }

    [Fact]
    public async Task The_2d_patch_adds_its_objects_once()
    {
        using var fixture = new SqliteInMemoryFixture();
        using (var ctx = fixture.CreateContext())
        {
            foreach (var sql in new[]
            {
                @"DROP TABLE ""FactRetractions"";",
                @"ALTER TABLE ""MedicationSuspensions"" DROP COLUMN ""RecordedAt"";",
                @"ALTER TABLE ""Medicines"" DROP COLUMN ""StockEpochFactId"";",
                @"ALTER TABLE ""NotificationEvents"" DROP COLUMN ""EpochFactId"";",
            })
            {
                await ctx.Database.ExecuteSqlRawAsync(sql);
            }
        }

        for (var run = 0; run < 2; run++)
        {
            using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        var id = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Medicines.SingleAsync(m => m.Id == id)).StockEpochFactId.Should().NotBeNull();
            (await ctx.FactRetractions.AnyAsync()).Should().BeFalse();
        }
    }

    private static async Task<Guid> SeedAsync(SqliteInMemoryFixture fixture)
    {
        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = await new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx, Clock),
                TestOperationLog.For(ctx), new UnitOfWork(ctx),
                Clock).ExecuteAsync(new AddMedicineCommand(
                    Name: "Enalapril", Unit: "compresse", DosePerAdministration: 1m, AdministrationsPerDay: 1,
                    StartDate: new DateOnly(2026, 9, 3), ThresholdDays: 7,
                    NotificationChannels: NotificationChannels.Windows, InitialQuantity: 50m), CancellationToken.None);
        }
        await TickAsync(fixture);
        return id;
    }

    private static async Task TickAsync(SqliteInMemoryFixture fixture)
    {
        await using var ctx = fixture.CreateContext();
        await new MedReminder.Application.Monitoring.ConsumptionCatchUp(
            new MedicineRepository(ctx), LedgerFor(ctx), new UnitOfWork(ctx)).RunAsync(CancellationToken.None);
    }

    private static FactHistoryQuery History(MedReminderDbContext ctx) => new(
        new MedicineRepository(ctx),
        new StockMovementRepository(ctx, Clock),
        new MedicationIntakeRepository(ctx),
        new StockCountRepository(ctx),
        new MedicationSuspensionRepository(ctx),
        new LedgerCutoffRepository(ctx),
        Clock);

    private static RetractFact Retract(MedReminderDbContext ctx, FactHistoryQuery history) => new(
        history,
        new MedicineRepository(ctx),
        new StockMovementRepository(ctx, Clock),
        new MedicationIntakeRepository(ctx),
        new StockCountRepository(ctx),
        new MedicationSuspensionRepository(ctx),
        new FactRetractionRepository(ctx),
        LedgerFor(ctx),
        TestOperationLog.For(ctx), new UnitOfWork(ctx),
        Clock);

    private static LedgerSynchronizer LedgerFor(MedReminderDbContext ctx)
    {
        var stock = new StockMovementRepository(ctx, Clock);
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
            Clock);
    }

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
