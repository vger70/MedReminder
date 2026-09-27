using FluentAssertions;
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

// B.1 Phase 2b boot patch (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §3.5, §4.2, §7.3): on a database created before it, every stock
// movement becomes Legacy, the cutoff is the day before the patch, and
// the existing slots become one set per medicine. Idempotent.
public class DatabaseInitializerLedgerPatchTests
{
    private static readonly DateTimeOffset PatchInstant = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid WithSlots = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid WithoutSlots = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    [Fact]
    public async Task Freezes_movements_stores_the_cutoff_and_groups_slots()
    {
        using var fixture = await PrePhase2bDatabaseAsync();

        await InitializeAsync(fixture, PatchInstant);

        using var ctx = fixture.CreateContext();
        (await ctx.StockMovements.ToListAsync()).Should().HaveCount(3)
            .And.OnlyContain(m => m.Origin == StockMovementOrigin.Legacy);

        var cutoff = await ctx.LedgerCutoffs.SingleAsync();
        cutoff.CutoffDay.Should().Be(new DateOnly(2026, 9, 19));
        cutoff.FrozenAt.Should().Be(PatchInstant);

        var set = await ctx.MedicationAdministrationSlotSets.SingleAsync();
        set.Id.Should().Be(WithSlots);
        set.MedicineId.Should().Be(WithSlots);
        set.EffectiveFrom.Should().Be(new DateOnly(2026, 9, 1));
        set.RecordedAt.Should().Be(PatchInstant);

        var slots = new MedicationAdministrationSlotRepository(ctx);
        (await slots.ListForMedicineAsync(WithSlots, default)).Select(s => s.Dose)
            .Should().Equal(1m, 0.5m);
        (await slots.ListForMedicineAsync(WithoutSlots, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Re_running_the_patch_changes_nothing()
    {
        using var fixture = await PrePhase2bDatabaseAsync();
        await InitializeAsync(fixture, PatchInstant);

        await InitializeAsync(fixture, PatchInstant.AddDays(3));

        using var ctx = fixture.CreateContext();
        (await ctx.LedgerCutoffs.SingleAsync()).CutoffDay.Should().Be(new DateOnly(2026, 9, 19));
        (await ctx.MedicationAdministrationSlotSets.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Rows_written_after_the_patch_keep_their_origin()
    {
        using var fixture = await PrePhase2bDatabaseAsync();
        await InitializeAsync(fixture, PatchInstant);

        using (var ctx = fixture.CreateContext())
        {
            ctx.StockMovements.Add(new StockMovement
            {
                MedicineId = WithSlots,
                OccurredAt = PatchInstant,
                Kind = StockMovementKind.NewPackage,
                QuantityDelta = 28m,
                StockEpoch = 2,
                Origin = StockMovementOrigin.User,
            });
            await ctx.SaveChangesAsync();
        }

        using var check = fixture.CreateContext();
        (await check.StockMovements.Where(m => m.Origin == StockMovementOrigin.User).CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task A_new_database_has_no_cutoff()
    {
        using var fixture = new SqliteInMemoryFixture();

        await InitializeAsync(fixture, PatchInstant);

        using var ctx = fixture.CreateContext();
        (await ctx.LedgerCutoffs.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_latest_recorded_set_is_the_current_one()
    {
        using var fixture = new SqliteInMemoryFixture();
        using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(NewMedicine(WithSlots));
            await ctx.SaveChangesAsync();

            var repo = new MedicationAdministrationSlotRepository(ctx);
            var first = NewSet(PatchInstant);
            await repo.AddSetAsync(first, [NewSlot(first, 1m, 0)], default);
            var second = NewSet(PatchInstant.AddHours(1));
            await repo.AddSetAsync(second, [NewSlot(second, 2m, 0), NewSlot(second, 3m, 1)], default);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = fixture.CreateContext())
        {
            var repo = new MedicationAdministrationSlotRepository(ctx);
            (await repo.ListForMedicineAsync(WithSlots, default)).Select(s => s.Dose)
                .Should().Equal(2m, 3m);

            await repo.AddSetAsync(NewSet(PatchInstant.AddHours(2)), [], default);
            await ctx.SaveChangesAsync();
            (await repo.ListForMedicineAsync(WithSlots, default)).Should().BeEmpty();
            (await ctx.MedicationAdministrationSlots.CountAsync()).Should().Be(3);
        }
    }

    [Fact]
    public async Task AddSetAsync_rejects_slots_of_another_set()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var ctx = fixture.CreateContext();
        var repo = new MedicationAdministrationSlotRepository(ctx);
        var set = NewSet(PatchInstant);

        await FluentActions.Awaiting(() => repo.AddSetAsync(set, [NewSlot(NewSet(PatchInstant), 1m, 0)], default))
            .Should().ThrowAsync<ArgumentException>();
    }

    // Current schema minus the Phase 2b objects, with one medicine that
    // has two slots, one without slots, and three movements. Rows are
    // written through EF Core first so their text formats are the real
    // ones.
    private static async Task<SqliteInMemoryFixture> PrePhase2bDatabaseAsync()
    {
        var fixture = new SqliteInMemoryFixture();
        using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(NewMedicine(WithSlots));
            ctx.Medicines.Add(NewMedicine(WithoutSlots));
            ctx.MedicationAdministrationSlots.Add(new MedicationAdministrationSlot
            {
                MedicineId = WithSlots, Dose = 0.5m, Time = new TimeOnly(20, 0), Order = 1,
            });
            ctx.MedicationAdministrationSlots.Add(new MedicationAdministrationSlot
            {
                MedicineId = WithSlots, Dose = 1m, Time = new TimeOnly(8, 0), Order = 0,
            });
            foreach (var (medicine, kind, delta) in new[]
            {
                (WithSlots, StockMovementKind.InitialLoad, 30m),
                (WithSlots, StockMovementKind.Consumption, -1.5m),
                (WithoutSlots, StockMovementKind.InitialLoad, 10m),
            })
            {
                ctx.StockMovements.Add(new StockMovement
                {
                    MedicineId = medicine,
                    OccurredAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                    Kind = kind,
                    QuantityDelta = delta,
                    StockEpoch = 1,
                    Origin = StockMovementOrigin.User,
                });
            }
            await ctx.SaveChangesAsync();

            foreach (var sql in new[]
            {
                @"DROP TABLE ""StockCounts"";",
                @"DROP TABLE ""LedgerCutoff"";",
                @"DROP TABLE ""MedicationAdministrationSlotSets"";",
                @"DROP INDEX ""IX_MedicationAdministrationSlots_SetId"";",
                @"ALTER TABLE ""MedicationAdministrationSlots"" DROP COLUMN ""SetId"";",
                @"ALTER TABLE ""StockMovements"" DROP COLUMN ""Origin"";",
            })
            {
                await ctx.Database.ExecuteSqlRawAsync(sql);
            }
        }
        return fixture;
    }

    private static async Task InitializeAsync(SqliteInMemoryFixture fixture, DateTimeOffset now)
    {
        using var ctx = fixture.CreateContext();
        var initializer = new DatabaseInitializer(
            ctx, NullLogger<DatabaseInitializer>.Instance, new TestTime(now));
        await initializer.InitializeAsync(CancellationToken.None);
    }

    private static Medicine NewMedicine(Guid id) => new()
    {
        Id = id,
        Name = "Sample",
        Unit = "tablet",
        DosePerAdministration = 1m,
        AdministrationsPerDay = 1,
        StartDate = new DateOnly(2026, 9, 1),
        ThresholdDays = 7,
        NotificationChannels = NotificationChannels.Windows,
        CreatedAt = PatchInstant,
        UpdatedAt = PatchInstant,
    };

    private static MedicationAdministrationSlotSet NewSet(DateTimeOffset recordedAt) => new()
    {
        MedicineId = WithSlots,
        EffectiveFrom = new DateOnly(2026, 9, 1),
        RecordedAt = recordedAt,
    };

    private static MedicationAdministrationSlot NewSlot(
        MedicationAdministrationSlotSet set, decimal dose, int order) => new()
    {
        MedicineId = set.MedicineId,
        SetId = set.Id,
        Dose = dose,
        Order = order,
    };

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
