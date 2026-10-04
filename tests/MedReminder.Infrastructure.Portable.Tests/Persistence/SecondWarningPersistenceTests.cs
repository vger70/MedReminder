using FluentAssertions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Second low-stock warning (docs/notes/EVOLUTION-PROPOSALS-2.md §3.1) on
// real SQLite: the stage of events and sent emails persists, the boot
// patch adds it to an older database with every row as a first stage,
// and the latest row of an instant is the later stage.
public class SecondWarningPersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateTimeOffset At = new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_stage_persists_and_breaks_ties_of_the_same_instant()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await AddRowsAsync(ctx, id, NotificationCycle.FirstStage);
            await AddRowsAsync(ctx, id, NotificationCycle.SecondStage);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await new NotificationEventRepository(ctx).GetLatestForMedicineAsync(id, CancellationToken.None))!
                .Stage.Should().Be(NotificationCycle.SecondStage);
            (await new SentEmailNotificationRepository(ctx).GetLatestForMedicineAsync(id, CancellationToken.None))!
                .Stage.Should().Be(NotificationCycle.SecondStage);
        }
    }

    [Fact]
    public async Task The_patch_adds_the_stage_once_and_older_rows_are_first_stage()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await AddRowsAsync(ctx, id, NotificationCycle.SecondStage);
            await ctx.SaveChangesAsync();
            await ctx.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""NotificationEvents"" DROP COLUMN ""Stage"";");
            await ctx.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""SentEmailNotifications"" DROP COLUMN ""Stage"";");
        }

        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.NotificationEvents.SingleAsync()).Stage.Should().Be(NotificationCycle.FirstStage);
            (await ctx.SentEmailNotifications.SingleAsync()).Stage.Should().Be(NotificationCycle.FirstStage);
        }
    }

    private static async Task AddRowsAsync(MedReminderDbContext ctx, Guid medicineId, int stage)
    {
        await new NotificationEventRepository(ctx).AddAsync(new NotificationEvent
        {
            MedicineId = medicineId,
            StockEpoch = 1,
            TriggeredAt = At,
            Channel = NotificationChannels.Windows,
            DaysRemainingAtSend = 3,
            Success = true,
            Stage = stage,
        }, CancellationToken.None);
        await new SentEmailNotificationRepository(ctx).AddAsync(new SentEmailNotification
        {
            MedicineId = medicineId,
            StockEpoch = 1,
            SentAt = At,
            Stage = stage,
        }, CancellationToken.None);
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
                StartDate: new DateOnly(2026, 9, 3), ThresholdDays: 7,
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
