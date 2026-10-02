using FluentAssertions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Administrative deadlines (docs/notes/EVOLUTION-PROPOSALS-2.md §3.6) on
// real SQLite: save, complete and delete through the use cases, the
// reminder dedup, the boot patch on an older database, the export
// mapping and the medicine deletion.
public class DeadlinePersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task A_deadline_is_saved_completed_and_deleted()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = await Save(ctx).ExecuteAsync(new SaveDeadlineCommand(
                null, medicine, DeadlineKind.TherapeuticPlan, "Plan", Today.AddDays(30), 14, 12,
                NotificationChannels.Email, null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new CompleteDeadline(new DeadlineRepository(ctx), Save(ctx))
                .ExecuteAsync(id, Today, CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            var stored = await ctx.Deadlines.SingleAsync();
            stored.MedicineId.Should().Be(medicine);
            stored.Kind.Should().Be(DeadlineKind.TherapeuticPlan);
            stored.Label.Should().Be("Plan");
            stored.DueOn.Should().Be(Today.AddDays(30).AddMonths(12));
            stored.LeadDays.Should().Be(14);
            stored.RepeatMonths.Should().Be(12);
            stored.Channels.Should().Be(NotificationChannels.Email);
            stored.DoneOn.Should().BeNull();
            await new DeleteDeadline(new DeadlineRepository(ctx), TestOperationLog.For(ctx),
                new UnitOfWork(ctx), Clock).ExecuteAsync(id, CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Deadlines.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_deadline_of_the_profile_has_no_medicine()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SaveDeadlineCommand(
                null, null, DeadlineKind.ExemptionRenewal, null, Today, 0, null,
                NotificationChannels.Windows, Today), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            var stored = await ctx.Deadlines.SingleAsync();
            stored.MedicineId.Should().BeNull();
            stored.DoneOn.Should().Be(Today);
        }
    }

    [Fact]
    public async Task A_reminder_is_found_by_deadline_and_date()
    {
        using var fixture = new SqliteInMemoryFixture();
        var deadline = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            await new DeadlineReminderEventRepository(ctx).AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = deadline, DueOn = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DeadlineReminderEventRepository(ctx);
            (await repo.ExistsAsync(deadline, Today, CancellationToken.None)).Should().BeTrue();
            (await repo.ExistsAsync(deadline, Today.AddDays(1), CancellationToken.None)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task The_patch_adds_the_deadline_tables_once()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""Deadlines"";");
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""DeadlineReminderEvents"";");
        }
        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SaveDeadlineCommand(
                null, null, DeadlineKind.CheckUp, null, Today, 7, 6, NotificationChannels.Both, null),
                CancellationToken.None);
            await new DeadlineReminderEventRepository(ctx).AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = Guid.NewGuid(), DueOn = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Deadlines.SingleAsync()).RepeatMonths.Should().Be(6);
            (await ctx.DeadlineReminderEvents.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_deadlines_and_reminders_only()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        Guid own;
        await using (var ctx = fixture.CreateContext())
        {
            var id = await Save(ctx).ExecuteAsync(new SaveDeadlineCommand(
                null, medicine, DeadlineKind.TherapeuticPlan, null, Today, 7, null, NotificationChannels.Both, null),
                CancellationToken.None);
            own = await Save(ctx).ExecuteAsync(new SaveDeadlineCommand(
                null, null, DeadlineKind.CheckUp, null, Today, 7, null, NotificationChannels.Both, null),
                CancellationToken.None);
            await new DeadlineReminderEventRepository(ctx).AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = id, MedicineId = medicine, DueOn = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await new DeadlineReminderEventRepository(ctx).AddAsync(new DeadlineReminderEvent
            {
                DeadlineId = own, DueOn = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            await new MedicineDeletionRepository(ctx).RemoveAsync(medicine, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Deadlines.SingleAsync()).Id.Should().Be(own);
            (await ctx.DeadlineReminderEvents.SingleAsync()).DeadlineId.Should().Be(own);
        }
    }

    [Fact]
    public void The_export_mapping_round_trips()
    {
        var d = new Deadline
        {
            MedicineId = Guid.NewGuid(), Kind = DeadlineKind.Other, Label = "Disability card", DueOn = Today,
            LeadDays = 30, RepeatMonths = 24, Channels = NotificationChannels.Windows, DoneOn = null,
            RecordedAt = Clock.GetUtcNow(), UpdatedAt = Clock.GetUtcNow(),
        };

        var dto = ExportMapper.ToDto(d);

        dto.Kind.Should().Be("Other");
        dto.Channels.Should().Be("Windows");
        ExportMapper.ToEntity(dto).Should().BeEquivalentTo(d);
    }

    private static SaveDeadline Save(MedReminderDbContext ctx)
        => new(new MedicineRepository(ctx), new DeadlineRepository(ctx), TestOperationLog.For(ctx),
            new UnitOfWork(ctx), Clock);

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
