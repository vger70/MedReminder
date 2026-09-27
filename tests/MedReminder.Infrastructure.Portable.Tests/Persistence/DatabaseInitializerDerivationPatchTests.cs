using FluentAssertions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// B.1 Phase 2c-2 boot patch (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §13): the facts the derivation reads, then a re-freeze of a database
// written by earlier versions, including rows written after the 2b
// patch (User / Derived origins, a 2b cutoff).
public class DatabaseInitializerDerivationPatchTests
{
    private static readonly DateTimeOffset PatchInstant = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Active = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Inactive = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    [Fact]
    public async Task Re_freezes_a_database_written_after_the_2b_patch()
    {
        using var fixture = await PrePhase2c2DatabaseAsync();

        await InitializeAsync(fixture, PatchInstant);

        using var ctx = fixture.CreateContext();
        (await ctx.StockMovements.ToListAsync()).Should().HaveCount(3)
            .And.OnlyContain(m => m.Origin == StockMovementOrigin.Legacy);

        var cutoff = await ctx.LedgerCutoffs.SingleAsync();
        cutoff.CutoffDay.Should().Be(new DateOnly(2026, 10, 4));
        cutoff.FrozenAt.Should().Be(PatchInstant);

        var medicines = await ctx.Medicines.ToDictionaryAsync(m => m.Id);
        medicines[Active].LedgerBaselineEpoch.Should().Be(3);
        medicines[Inactive].LedgerBaselineEpoch.Should().Be(1);

        var activity = await ctx.MedicineActivityChanges.SingleAsync();
        activity.MedicineId.Should().Be(Inactive);
        activity.Day.Should().Be(new DateOnly(2026, 10, 5));
        activity.Active.Should().BeFalse();

        var intake = await ctx.MedicationIntakes.SingleAsync();
        intake.RecordedAt.Should().BeBefore(PatchInstant, "an intake written before the patch is Legacy");
        (await ctx.MedicationScheduleHistories.SingleAsync()).RecordedAt.Should().Be(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Re_running_the_patch_does_not_freeze_again()
    {
        using var fixture = await PrePhase2c2DatabaseAsync();
        await InitializeAsync(fixture, PatchInstant);

        using (var ctx = fixture.CreateContext())
        {
            ctx.StockMovements.Add(new StockMovement
            {
                MedicineId = Active,
                OccurredAt = PatchInstant.AddHours(1),
                Kind = StockMovementKind.NewPackage,
                QuantityDelta = 28m,
                StockEpoch = 4,
                Origin = StockMovementOrigin.User,
            });
            await ctx.SaveChangesAsync();
        }

        await InitializeAsync(fixture, PatchInstant.AddDays(2));

        using var check = fixture.CreateContext();
        (await check.LedgerCutoffs.SingleAsync()).FrozenAt.Should().Be(PatchInstant);
        (await check.StockMovements.CountAsync(m => m.Origin == StockMovementOrigin.User)).Should().Be(1);
        (await check.MedicineActivityChanges.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_new_database_is_not_frozen()
    {
        using var fixture = new SqliteInMemoryFixture();

        await InitializeAsync(fixture, PatchInstant);

        using var ctx = fixture.CreateContext();
        (await ctx.LedgerCutoffs.AnyAsync()).Should().BeFalse();
    }

    // Current schema minus the 2c-2 objects: two medicines (one
    // inactive), movements labeled by the 2b code, a 2b cutoff, one
    // intake and one schedule row.
    private static async Task<SqliteInMemoryFixture> PrePhase2c2DatabaseAsync()
    {
        var fixture = new SqliteInMemoryFixture();
        using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(NewMedicine(Active, isActive: true, epoch: 3));
            ctx.Medicines.Add(NewMedicine(Inactive, isActive: false, epoch: 1));
            foreach (var (kind, delta, origin) in new[]
            {
                (StockMovementKind.InitialLoad, 30m, StockMovementOrigin.Legacy),
                (StockMovementKind.NewPackage, 28m, StockMovementOrigin.User),
                (StockMovementKind.NegativeCorrection, -2m, StockMovementOrigin.Derived),
            })
            {
                ctx.StockMovements.Add(new StockMovement
                {
                    MedicineId = Active,
                    OccurredAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                    Kind = kind,
                    QuantityDelta = delta,
                    StockEpoch = 1,
                    Origin = origin,
                });
            }
            ctx.MedicationIntakes.Add(new MedicationIntake
            {
                MedicineId = Active, Day = new DateOnly(2026, 9, 20), Status = IntakeStatus.Taken, Quantity = 1m,
            });
            ctx.MedicationScheduleHistories.Add(new MedicationScheduleHistory
            {
                MedicineId = Active, EffectiveFrom = new DateOnly(2026, 9, 1),
                DosePerAdministration = 1m, AdministrationsPerDay = 1,
            });
            ctx.LedgerCutoffs.Add(new LedgerCutoff
            {
                CutoffDay = new DateOnly(2026, 9, 25),
                FrozenAt = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero),
            });
            await ctx.SaveChangesAsync();

            foreach (var sql in new[]
            {
                @"DROP TABLE ""MedicineActivityChanges"";",
                @"ALTER TABLE ""MedicationIntakes"" DROP COLUMN ""RecordedAt"";",
                @"ALTER TABLE ""MedicationScheduleHistories"" DROP COLUMN ""RecordedAt"";",
                @"ALTER TABLE ""Medicines"" DROP COLUMN ""LedgerBaselineEpoch"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""Notes"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""LedgerAtStartOfDay"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""CountDayScheduled"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""Correction"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""MaterializesCountDay"";",
                @"ALTER TABLE ""StockCounts"" DROP COLUMN ""AdvancesEpoch"";",
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
        await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, new TestTime(now))
            .InitializeAsync(CancellationToken.None);
    }

    private static Medicine NewMedicine(Guid id, bool isActive, int epoch) => new()
    {
        Id = id,
        Name = "Sample",
        Unit = "tablet",
        DosePerAdministration = 1m,
        AdministrationsPerDay = 1,
        StartDate = new DateOnly(2026, 9, 1),
        ThresholdDays = 7,
        IsActive = isActive,
        StockEpoch = epoch,
        NotificationChannels = NotificationChannels.Windows,
        CreatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
    };

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
