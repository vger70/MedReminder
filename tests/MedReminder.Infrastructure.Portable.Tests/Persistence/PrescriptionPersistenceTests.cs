using FluentAssertions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Prescription lifecycle (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2) on
// real SQLite: save, change and delete through the use cases, the
// reminder dedup, the boot patch on an older database, the export
// mapping and the medicine deletion.
public class PrescriptionPersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task A_prescription_is_saved_changed_and_deleted()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        Guid id;
        await using (var ctx = fixture.CreateContext())
        {
            id = await Save(ctx).ExecuteAsync(new SavePrescriptionCommand(
                null, medicine, Today, Today, "NRE-1", 2, Today.AddDays(29), null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SavePrescriptionCommand(
                id, medicine, Today, Today, "NRE-1", 2, Today.AddDays(29), Today), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            var stored = await ctx.Prescriptions.SingleAsync();
            stored.CollectedOn.Should().Be(Today);
            stored.Code.Should().Be("NRE-1");
            stored.ValidUntil.Should().Be(Today.AddDays(29));
            await new DeletePrescription(new PrescriptionRepository(ctx), TestOperationLog.For(ctx),
                new UnitOfWork(ctx), Clock).ExecuteAsync(id, CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Prescriptions.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_reminder_is_found_by_prescription_and_end_date()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        var prescription = Guid.NewGuid();
        await using (var ctx = fixture.CreateContext())
        {
            await new PrescriptionReminderEventRepository(ctx).AddAsync(new PrescriptionReminderEvent
            {
                PrescriptionId = prescription, MedicineId = medicine, ValidUntil = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new PrescriptionReminderEventRepository(ctx);
            (await repo.ExistsAsync(prescription, Today, CancellationToken.None)).Should().BeTrue();
            (await repo.ExistsAsync(prescription, Today.AddDays(1), CancellationToken.None)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task The_patch_adds_the_prescription_tables_once()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""Prescriptions"";");
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""PrescriptionReminderEvents"";");
        }
        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        var medicine = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await Save(ctx).ExecuteAsync(new SavePrescriptionCommand(
                null, medicine, Today, null, null, null, null, null), CancellationToken.None);
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Prescriptions.SingleAsync()).RequestedOn.Should().Be(Today);
        }
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_prescriptions_and_reminders()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicine = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            var id = await Save(ctx).ExecuteAsync(new SavePrescriptionCommand(
                null, medicine, Today, Today, null, null, Today, null), CancellationToken.None);
            await new PrescriptionReminderEventRepository(ctx).AddAsync(new PrescriptionReminderEvent
            {
                PrescriptionId = id, MedicineId = medicine, ValidUntil = Today, FiredAt = Clock.GetUtcNow(),
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
            (await ctx.Prescriptions.AnyAsync()).Should().BeFalse();
            (await ctx.PrescriptionReminderEvents.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Shortage_notices_are_kept_once_patched_and_removed_with_the_medicine()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"DROP TABLE ""ShortageNoticeEvents"";");
        }
        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }
        var medicine = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await new ShortageNoticeEventRepository(ctx).AddAsync(new MedReminder.Domain.Catalogue.ShortageNoticeEvent
            {
                MedicineId = medicine, Code = "045348036", Start = Today, FiredAt = Clock.GetUtcNow(),
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            var repo = new ShortageNoticeEventRepository(ctx);
            (await repo.ExistsAsync(medicine, "045348036", Today, CancellationToken.None)).Should().BeTrue();
            (await repo.ExistsAsync(medicine, "045348036", Today.AddDays(1), CancellationToken.None)).Should().BeFalse();
            await new MedicineDeletionRepository(ctx).RemoveAsync(medicine, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.ShortageNoticeEvents.AnyAsync()).Should().BeFalse();
        }
    }

    [Fact]
    public void The_export_mapping_round_trips()
    {
        var p = new Prescription
        {
            MedicineId = Guid.NewGuid(), RequestedOn = Today, IssuedOn = Today, Code = "NRE", Packages = 2,
            ValidUntil = Today.AddDays(29), CollectedOn = Today.AddDays(3),
            RecordedAt = Clock.GetUtcNow(), UpdatedAt = Clock.GetUtcNow(),
        };

        ExportMapper.ToEntity(ExportMapper.ToDto(p)).Should().BeEquivalentTo(p);
    }

    private static SavePrescription Save(MedReminderDbContext ctx)
        => new(new MedicineRepository(ctx), new PrescriptionRepository(ctx), TestOperationLog.For(ctx),
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
