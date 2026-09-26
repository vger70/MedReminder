using System.Data.Common;
using FluentAssertions;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Verifies the A5 additive schema patch and the DoseReminderEvents
// dedup store (ANALYSIS-A5 §3.2 / §3.3):
//   Medicines.RemindOnDose (INTEGER NOT NULL DEFAULT 0)
//   DoseReminderEvents table + unique (MedicineId, SlotKey, LocalDate).
public class DoseReminderEventStoreTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public async Task Patch_adds_column_and_table_to_pre_A5_shape()
    {
        using var fixture = new SqliteInMemoryFixture();

        // Simulate a pre-A5 shape by removing the new column and table
        // from the fresh EF-created schema.
        using (var ctx = fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Medicines\" DROP COLUMN \"RemindOnDose\";");
            await ctx.Database.ExecuteSqlRawAsync(
                "DROP TABLE \"DoseReminderEvents\";");
        }

        (await ColumnExistsAsync(fixture, "Medicines", "RemindOnDose")).Should().BeFalse();
        (await TableExistsAsync(fixture, "DoseReminderEvents")).Should().BeFalse();

        using (var ctx = fixture.CreateContext())
        {
            var initializer = new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);
        }

        (await ColumnExistsAsync(fixture, "Medicines", "RemindOnDose")).Should().BeTrue();
        (await TableExistsAsync(fixture, "DoseReminderEvents")).Should().BeTrue();
    }

    [Fact]
    public async Task Patch_is_idempotent_when_re_run_on_current_schema()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var ctx = fixture.CreateContext())
        {
            var initializer = new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance);
            await initializer.InitializeAsync(CancellationToken.None);
            await initializer.InitializeAsync(CancellationToken.None);
        }

        (await ColumnExistsAsync(fixture, "Medicines", "RemindOnDose")).Should().BeTrue();
        (await TableExistsAsync(fixture, "DoseReminderEvents")).Should().BeTrue();
    }

    [Fact]
    public async Task Repository_round_trips_and_deduplicates()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();
        var slotKey = Guid.NewGuid().ToString();

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseReminderEventRepository(ctx);
            await repo.AddAsync(new DoseReminderEvent
            {
                MedicineId = medicineId,
                SlotKey = slotKey,
                LocalDate = Today,
                FiredAt = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero),
                Channel = NotificationChannels.Windows,
            }, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseReminderEventRepository(ctx);
            (await repo.ExistsAsync(medicineId, slotKey, Today, CancellationToken.None))
                .Should().BeTrue();
            // Different day → not deduplicated.
            (await repo.ExistsAsync(medicineId, slotKey, Today.AddDays(1), CancellationToken.None))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_key()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();
        var slotKey = Guid.NewGuid().ToString();

        DoseReminderEvent Make() => new()
        {
            MedicineId = medicineId,
            SlotKey = slotKey,
            LocalDate = Today,
            FiredAt = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero),
            Channel = NotificationChannels.Windows,
        };

        await using (var ctx = fixture.CreateContext())
        {
            ctx.DoseReminderEvents.Add(Make());
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            ctx.DoseReminderEvents.Add(Make());
            var act = async () => await ctx.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }

    [Fact]
    public async Task Prune_removes_only_rows_older_than_cutoff()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.DoseReminderEvents.AddRange(
                Row(medicineId, "old", Today.AddDays(-40)),
                Row(medicineId, "recent", Today.AddDays(-1)));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new DoseReminderEventRepository(ctx);
            await repo.PruneOlderThanAsync(Today.AddDays(-30), CancellationToken.None);
        }

        await using (var ctx = fixture.CreateContext())
        {
            var remaining = await ctx.DoseReminderEvents.AsNoTracking().ToListAsync();
            remaining.Should().ContainSingle();
            remaining[0].SlotKey.Should().Be("recent");
        }
    }

    private static DoseReminderEvent Row(Guid medicineId, string slotKey, DateOnly date)
        => new()
        {
            MedicineId = medicineId,
            SlotKey = slotKey,
            LocalDate = date,
            FiredAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            Channel = NotificationChannels.Windows,
        };

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
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteInMemoryFixture fixture, string table)
    {
        using var ctx = fixture.CreateContext();
        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = table;
        cmd.Parameters.Add(p);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return count > 0;
    }
}
