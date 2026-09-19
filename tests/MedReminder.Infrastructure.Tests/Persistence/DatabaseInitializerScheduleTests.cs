using System.Data.Common;
using FluentAssertions;
using MedReminder.Domain.Medicines;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Verifies the A1 additive schema patch:
//   MedicationScheduleHistories.ScheduleKind (INTEGER NOT NULL DEFAULT 0)
//   MedicationScheduleHistories.SchedulePayload (TEXT NULL)
// Pre-A1 rows must survive the patch and read back as FixedDaily
// (ScheduleKind = 0, SchedulePayload = NULL) so the projection engine
// continues to compute dose × administrations from the legacy columns.
public class DatabaseInitializerScheduleTests
{
    [Fact]
    public async Task Adds_missing_columns_and_preserves_pre_A1_rows()
    {
        using var fixture = new SqliteInMemoryFixture();

        // Simulate a pre-A1 shape by dropping the two new columns from
        // the fresh EF-created schema. Requires SQLite >= 3.35;
        // Microsoft.Data.Sqlite bundles a modern engine that supports
        // ALTER TABLE DROP COLUMN.
        using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"MedicationScheduleHistories\" DROP COLUMN \"ScheduleKind\";");
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"MedicationScheduleHistories\" DROP COLUMN \"SchedulePayload\";");
        }

        // Insert a legacy row so we can verify preservation later.
        var legacyMedicineId = Guid.NewGuid();
        var legacyEntryId = Guid.NewGuid();
        using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""Medicines""
                    (""Id"", ""Name"", ""Unit"", ""DosePerAdministration"",
                     ""AdministrationsPerDay"", ""StartDate"", ""ThresholdDays"",
                     ""IsActive"", ""StockEpoch"", ""NotificationChannels"",
                     ""CreatedAt"", ""UpdatedAt"")
                VALUES ({0}, 'Enalapril', 'compresse', '1',
                        2, '2026-01-01', 7,
                        1, 1, 1,
                        0, 0);",
                legacyMedicineId.ToString());

            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""MedicationScheduleHistories""
                    (""Id"", ""MedicineId"", ""EffectiveFrom"",
                     ""DosePerAdministration"", ""AdministrationsPerDay"")
                VALUES ({0}, {1}, '2026-01-01', '1', 2);",
                legacyEntryId.ToString(),
                legacyMedicineId.ToString());
        }

        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "ScheduleKind"))
            .Should().BeFalse("the pre-A1 shape has no ScheduleKind column");
        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "SchedulePayload"))
            .Should().BeFalse("the pre-A1 shape has no SchedulePayload column");

        // Run the initializer against the pre-A1 DB.
        using (var ctx = fixture.CreateContext())
        {
            var initializer = new DatabaseInitializer(
                ctx,
                NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);
        }

        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "ScheduleKind"))
            .Should().BeTrue();
        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "SchedulePayload"))
            .Should().BeTrue();

        // The legacy row must read back with ScheduleKind = FixedDaily
        // (default 0) and SchedulePayload = NULL, so the codec falls
        // back to the legacy Dose / AdministrationsPerDay columns.
        using (var ctx = fixture.CreateContext())
        {
            var entry = await ctx.MedicationScheduleHistories.SingleAsync();
            entry.Id.Should().Be(legacyEntryId);
            entry.ScheduleKind.Should().Be(ScheduleKind.FixedDaily);
            entry.SchedulePayload.Should().BeNull();
            entry.DosePerAdministration.Should().Be(1m);
            entry.AdministrationsPerDay.Should().Be(2);
        }
    }

    [Fact]
    public async Task Patch_is_idempotent_when_re_run_on_current_schema()
    {
        // On a fresh DB the columns are already created by
        // EnsureCreated. Running the initializer must not fail nor
        // duplicate work.
        using var fixture = new SqliteInMemoryFixture();

        using (var ctx = fixture.CreateContext())
        {
            var initializer = new DatabaseInitializer(
                ctx,
                NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.InitializeAsync(CancellationToken.None);
        }

        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "ScheduleKind"))
            .Should().BeTrue();
        (await ColumnExistsAsync(fixture, "MedicationScheduleHistories", "SchedulePayload"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Round_trips_a_cyclic_schedule_via_ef_core()
    {
        using var fixture = new SqliteInMemoryFixture();

        var medicineId = Guid.NewGuid();
        var cyclic = new CyclicSchedule(onDays: 21, offDays: 7, quantityPerOnDay: 1m);
        var (kind, payload) = ScheduleCodec.Serialize(cyclic);

        using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(new Medicine
            {
                Id = medicineId,
                Name = "Cyclic pill",
                Unit = "compresse",
                DosePerAdministration = 1m,
                AdministrationsPerDay = 1,
                StartDate = new DateOnly(2026, 3, 1),
                ThresholdDays = 7,
                CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            });
            ctx.MedicationScheduleHistories.Add(new MedicationScheduleHistory
            {
                MedicineId = medicineId,
                EffectiveFrom = new DateOnly(2026, 3, 1),
                DosePerAdministration = 1m,
                AdministrationsPerDay = 1,
                ScheduleKind = kind,
                SchedulePayload = payload,
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = fixture.CreateContext())
        {
            var entry = await ctx.MedicationScheduleHistories.SingleAsync();
            entry.ScheduleKind.Should().Be(ScheduleKind.Cyclic);
            entry.SchedulePayload.Should().NotBeNullOrWhiteSpace();

            var restored = ScheduleCodec.Deserialize(
                entry.ScheduleKind, entry.SchedulePayload,
                entry.DosePerAdministration, entry.AdministrationsPerDay);
            restored.Should().Be(cyclic);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteInMemoryFixture fixture, string table, string column)
    {
        using var ctx = fixture.CreateContext();
        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
