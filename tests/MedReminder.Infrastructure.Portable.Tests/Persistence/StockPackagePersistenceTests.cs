using FluentAssertions;
using MedReminder.Application.Packages;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Packages (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md) on real SQLite:
// save, discard and delete through the use cases, the boot patch on an
// older database, the medicine deletion and the export mapping.
public class StockPackagePersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task A_package_is_saved_with_every_field()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        Guid id;
        var movement = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            id = await Save(ctx).ExecuteAsync(new SaveStockPackageCommand(
                null, medicine, 2.5m, new DateOnly(2027, 3, 31), 28, Today, "L2345", MovementId: movement),
                CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            var stored = await ctx.StockPackages.SingleAsync();
            stored.Id.Should().Be(id);
            stored.MedicineId.Should().Be(medicine);
            stored.MovementId.Should().Be(movement);
            stored.Quantity.Should().Be(2.5m);
            stored.ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));
            stored.UseWithinDays.Should().Be(28);
            stored.OpenedOn.Should().Be(Today);
            stored.Batch.Should().Be("L2345");
            stored.ClosedOn.Should().BeNull();
            stored.Closure.Should().BeNull();
        }
    }

    [Fact]
    public async Task A_discard_closes_the_package_and_corrects_the_stock_together()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = await Save(ctx).ExecuteAsync(new SaveStockPackageCommand(
                null, medicine, 28m, new DateOnly(2026, 9, 30), null, null, null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await Discard(ctx).ExecuteAsync(new DiscardStockPackageCommand(id, Today, 20m), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            var stored = await ctx.StockPackages.SingleAsync();
            stored.Closure.Should().Be(PackageClosure.Discarded);
            stored.ClosedOn.Should().Be(Today);
            MedicineStock.Current(await ctx.StockMovements.AsNoTracking().ToListAsync()).Should().Be(30m);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new DeleteStockPackage(new StockPackageRepository(ctx), TestOperationLog.For(ctx),
                new UnitOfWork(ctx), Clock).ExecuteAsync(id, CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.StockPackages.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task The_patch_adds_the_package_table_once()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""StockPackages"";");
        }
        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SaveStockPackageCommand(
                null, medicine, 28m, new DateOnly(2027, 3, 31), null, null, null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.StockPackages.SingleAsync()).ExpiresOn.Should().Be(new DateOnly(2027, 3, 31));
        }
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_packages()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        var other = await SeedAsync(fixture);
        Guid own;
        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SaveStockPackageCommand(
                null, medicine, 28m, null, null, null, null), CancellationToken.None);
            own = await Save(ctx).ExecuteAsync(new SaveStockPackageCommand(
                null, other, 28m, null, null, null, null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new MedicineDeletionRepository(ctx).RemoveAsync(medicine, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.StockPackages.SingleAsync()).Id.Should().Be(own);
        }
    }

    [Fact]
    public void The_export_mapping_round_trips()
    {
        var p = new StockPackage
        {
            MedicineId = Guid.NewGuid(), MovementId = Guid.NewGuid(), Quantity = 2.5m,
            ExpiresOn = new DateOnly(2027, 3, 31), UseWithinDays = 28, OpenedOn = Today, Batch = "L2345",
            ClosedOn = Today, Closure = PackageClosure.Finished,
            RecordedAt = Clock.GetUtcNow(), UpdatedAt = Clock.GetUtcNow(),
        };

        var dto = ExportMapper.ToDto(p);

        dto.Closure.Should().Be("Finished");
        ExportMapper.ToEntity(dto).Should().BeEquivalentTo(p);
        ExportMapper.ToEntity(ExportMapper.ToDto(new StockPackage { MedicineId = p.MedicineId, Quantity = 1m }))
            .Closure.Should().BeNull();
    }

    [Theory]
    [InlineData("Returned")]
    [InlineData("7")]
    public void A_closure_from_a_newer_app_imports_as_finished(string closure)
    {
        var dto = ExportMapper.ToDto(new StockPackage
        {
            MedicineId = Guid.NewGuid(), Quantity = 1m, ClosedOn = Today, Closure = PackageClosure.Finished,
        });
        dto.Closure = closure;

        ExportMapper.ToEntity(dto).Closure.Should().Be(PackageClosure.Finished);
    }

    private static SaveStockPackage Save(MedReminderDbContext ctx)
        => new(new MedicineRepository(ctx), new StockPackageRepository(ctx), TestOperationLog.For(ctx),
            new UnitOfWork(ctx), Clock);

    private static DiscardStockPackage Discard(MedReminderDbContext ctx)
    {
        var log = TestOperationLog.For(ctx);
        var uow = new UnitOfWork(ctx);
        var stock = new StockMovementRepository(ctx, Clock);
        return new DiscardStockPackage(new StockPackageRepository(ctx), log, uow,
            new AdjustStockDown(new MedicineRepository(ctx), stock, log, uow, Clock), Clock);
    }

    private static async Task<Guid> SeedAsync(SqliteInMemoryFixture fixture)
    {
        await using var ctx = fixture.CreateContext();
        return await new AddMedicine(
            new MedicineRepository(ctx),
            new MedicationScheduleHistoryRepository(ctx),
            new MedicationAdministrationSlotRepository(ctx),
            new StockMovementRepository(ctx, Clock),
            TestOperationLog.For(ctx), new UnitOfWork(ctx),
            Clock).ExecuteAsync(new AddMedicineCommand(
                Name: "Enalapril", Unit: "compresse", DosePerAdministration: 1m, AdministrationsPerDay: 1,
                StartDate: Today, ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows, InitialQuantity: 50m), CancellationToken.None);
    }

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
