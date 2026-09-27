using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// B.1 Phase 3a on real SQLite: the SyncOperations boot patch, and
// operations committed with the use case that produced them.
public class SyncOperationPersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    private static readonly SyncSettings Enabled = new(
        Guid.Parse("0a0a0a0a-0000-0000-0000-000000000001"), Guid.Parse("0d0d0d0d-0000-0000-0000-000000000001"), 1);

    [Fact]
    public async Task Boot_patch_adds_the_sync_tables_to_an_older_database_and_is_idempotent()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""SyncOperations"";");
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""SyncFieldVersions"";");
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""SyncConflicts"";");
        }

        await InitializeAsync(fixture);
        await InitializeAsync(fixture);

        await using (var ctx = fixture.CreateContext())
        {
            await new SyncOperationRepository(ctx).AddAsync(new SyncOperation
            {
                HlcPhysicalMs = 1, HlcCounter = 0, DeviceId = Enabled.DeviceId, Generation = 1,
                Type = "MedicineFieldChanged", SchemaVersion = 1, MedicineId = Guid.NewGuid(), Payload = "{}",
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.SyncOperations.SingleAsync()).SegmentSeq.Should().BeNull();
            var indexes = await ctx.Database
                .SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM sqlite_master WHERE type = 'index' AND tbl_name = 'SyncOperations'")
                .ToListAsync();
            indexes.Should().Contain(["IX_SyncOperations_HlcPhysicalMs_HlcCounter", "IX_SyncOperations_SegmentSeq"]);
            (await ctx.SyncFieldVersions.CountAsync()).Should().Be(0);
            (await ctx.SyncConflicts.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Use_case_operations_commit_with_the_write_and_the_clock_resumes_from_the_log()
    {
        using var fixture = new SqliteInMemoryFixture();
        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = await new AddMedicine(
                new MedicineRepository(ctx),
                new MedicationScheduleHistoryRepository(ctx),
                new MedicationAdministrationSlotRepository(ctx),
                new StockMovementRepository(ctx, Clock),
                TestOperationLog.For(ctx, Clock, Enabled), new UnitOfWork(ctx),
                Clock).ExecuteAsync(new AddMedicineCommand(
                    "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 3), 7,
                    NotificationChannels.Windows, InitialQuantity: 50m), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new AddStock(new MedicineRepository(ctx), new StockMovementRepository(ctx, Clock),
                    TestOperationLog.For(ctx, Clock, Enabled), new UnitOfWork(ctx), Clock)
                .ExecuteAsync(new AddStockCommand(id, 28m, StockMovementKind.NewPackage), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new SyncOperationRepository(ctx);
            var log = await repo.ListAllAsync(CancellationToken.None);
            log.Select(o => o.Type).Should().Equal(
                "MedicineCreated", "ScheduleRowRecorded", "StockEntryRecorded", "StockEntryRecorded");
            log.Select(o => o.HlcCounter).Should().Equal(0, 1, 2, 3); // same wall-clock millisecond
            (await repo.GetLatestTimestampAsync(CancellationToken.None)).Should().Be(log[^1].Timestamp);

            var refill = (StockEntryRecorded)OperationCodec.Deserialize(log[3].Type, log[3].SchemaVersion, log[3].Payload);
            (await ctx.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.NewPackage)).Id
                .Should().Be(refill.MovementId);
        }
    }

    [Fact]
    public async Task A_failed_use_case_leaves_no_operation()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();

        await FluentActions.Awaiting(() => new AddStock(new MedicineRepository(ctx), new StockMovementRepository(ctx, Clock),
                    TestOperationLog.For(ctx, Clock, Enabled), new UnitOfWork(ctx), Clock)
                .ExecuteAsync(new AddStockCommand(Guid.NewGuid(), 28m, StockMovementKind.NewPackage), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        (await ctx.SyncOperations.CountAsync()).Should().Be(0);
    }

    private static async Task InitializeAsync(SqliteInMemoryFixture fixture)
    {
        await using var ctx = fixture.CreateContext();
        await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
            .InitializeAsync(CancellationToken.None);
    }

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
