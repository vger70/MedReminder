using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Application.Migrations;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// As-needed slots and extra intakes on real SQLite
// (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md §5): the flags
// persist, the boot patch adds them to an older database and marks the
// one-time backfill pending, a fresh database has nothing pending, and
// the archive carries the flags as additive fields.
public class AsNeededPersistenceTests
{
    private static readonly TestTime Clock = new(new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_flags_persist()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            ctx.MedicationIntakes.Add(new MedicationIntake
            {
                MedicineId = id, Day = new DateOnly(2026, 9, 13), Quantity = 1m,
                Status = IntakeStatus.Taken, RecordedAt = Clock.GetUtcNow(), IsExtra = true,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var slots = await new MedicationAdministrationSlotRepository(ctx).ListForMedicineAsync(id, default);
            slots.Select(s => s.IsAsNeeded).Should().Equal(false, true);
            (await ctx.MedicationIntakes.SingleAsync()).IsExtra.Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_fresh_database_has_no_pending_backfill()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();

        (await new PendingDataMigrations(ctx).IsPendingAsync(AsNeededSlotBackfill.MigrationName, default))
            .Should().BeFalse();
    }

    [Fact]
    public async Task The_patch_adds_the_flags_once_and_marks_the_backfill_pending()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SeedAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""MedicationAdministrationSlots"" DROP COLUMN ""IsAsNeeded"";");
            await ctx.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""MedicationIntakes"" DROP COLUMN ""IsExtra"";");
        }

        for (var run = 0; run < 2; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance, Clock)
                .InitializeAsync(CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.MedicationAdministrationSlots.ToListAsync()).Should().OnlyContain(s => !s.IsAsNeeded);
            var pending = new PendingDataMigrations(ctx);
            (await pending.IsPendingAsync(AsNeededSlotBackfill.MigrationName, default)).Should().BeTrue();

            await pending.CompleteAsync(AsNeededSlotBackfill.MigrationName, default);
            (await pending.IsPendingAsync(AsNeededSlotBackfill.MigrationName, default)).Should().BeFalse();
        }
    }

    [Fact]
    public void The_flags_round_trip_through_the_archive()
    {
        var slot = new MedicationAdministrationSlot
        {
            MedicineId = Guid.NewGuid(), SetId = Guid.NewGuid(), Dose = 1m, IsAsNeeded = true,
        };
        var intake = new MedicationIntake
        {
            MedicineId = Guid.NewGuid(), Day = new DateOnly(2026, 9, 13), Quantity = 1m,
            Status = IntakeStatus.Taken, IsExtra = true,
        };

        ExportMapper.ToEntity(ExportMapper.ToDto(slot)).IsAsNeeded.Should().BeTrue();
        ExportMapper.ToEntity(ExportMapper.ToDto(intake)).IsExtra.Should().BeTrue();
    }

    [Fact]
    public void An_archive_without_the_flags_imports_them_as_false()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var slot = JsonSerializer.Deserialize<ExportedAdministrationSlot>(
            "{\"id\":\"0f0f0f0f-0000-0000-0000-000000000002\",\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"," +
            "\"setId\":\"0c0c0c0c-0000-0000-0000-000000000003\",\"dose\":1,\"time\":null,\"timingLabel\":\"x\",\"order\":0}",
            options)!;
        var intake = JsonSerializer.Deserialize<ExportedIntake>(
            "{\"id\":\"0f0f0f0f-0000-0000-0000-000000000002\",\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"," +
            "\"day\":\"2026-09-13\",\"quantity\":1,\"status\":\"Taken\",\"notes\":null}",
            options)!;

        ExportMapper.ToEntity(slot).IsAsNeeded.Should().BeFalse();
        ExportMapper.ToEntity(intake).IsExtra.Should().BeFalse();
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
                Name: "Paracetamolo", Unit: "compresse", DosePerAdministration: 1m, AdministrationsPerDay: 1,
                StartDate: new DateOnly(2026, 9, 3), ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows, InitialQuantity: 20m,
                AdministrationSlots:
                [
                    new AdministrationSlotInput(1m, new TimeOnly(8, 0), null),
                    new AdministrationSlotInput(1m, null, "Al bisogno", IsAsNeeded: true),
                ]), CancellationToken.None);
    }

    private sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
