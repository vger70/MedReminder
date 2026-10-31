using FluentAssertions;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Issue #328: a database upgraded through DatabaseInitializer has the
// same tables, columns and indexes as one EnsureCreated builds now.
public class ModelIndexPatchTests
{
    // The first schema (Increment 3, commit 5a9b1a9): six tables, their
    // columns before any patch, and the indexes EnsureCreated gave them.
    private static readonly string[] OldestTables =
    [
        "Medicines", "StockMovements", "MedicationScheduleHistories", "MedicationSuspensions",
        "MedicationIntakes", "NotificationEvents",
    ];

    private static readonly string[] OldestIndexes =
    [
        "IX_Medicines_IsActive", "IX_Medicines_Name",
        "IX_StockMovements_MedicineId", "IX_StockMovements_MedicineId_Kind_OccurredAt",
        "IX_MedicationScheduleHistories_MedicineId_EffectiveFrom",
        "IX_MedicationSuspensions_MedicineId", "IX_MedicationSuspensions_MedicineId_EndDate",
        "IX_MedicationIntakes_MedicineId", "IX_MedicationIntakes_MedicineId_ScheduledAt",
        "IX_NotificationEvents_MedicineId", "IX_NotificationEvents_MedicineId_TriggeredAt",
    ];

    // The columns the patches added to the first six tables.
    private static readonly (string Table, string Column)[] AddedColumns =
    [
        ("Medicines", "NationalCode"), ("Medicines", "AtcCode"), ("Medicines", "LinkedReferenceMedicineId"),
        ("Medicines", "RemindOnDose"), ("Medicines", "StockEpochFactId"), ("Medicines", "LedgerBaselineEpoch"),
        ("StockMovements", "Origin"),
        ("MedicationScheduleHistories", "ScheduleKind"), ("MedicationScheduleHistories", "SchedulePayload"),
        ("MedicationScheduleHistories", "RecordedAt"),
        ("MedicationSuspensions", "RecordedAt"),
        ("MedicationIntakes", "Day"), ("MedicationIntakes", "IsExtra"), ("MedicationIntakes", "SlotTime"),
        ("MedicationIntakes", "RecordedAt"),
        ("NotificationEvents", "Stage"), ("NotificationEvents", "EpochFactId"),
    ];

    // The patches run from the second boot of a new database: they must
    // add nothing to what EnsureCreated built (the first dedup patch added
    // a second unique index on DoseReminderEvents).
    [Fact]
    public async Task A_second_boot_adds_no_index_to_a_new_database()
    {
        using var fixture = new SqliteInMemoryFixture();
        List<string> created;
        await using (var ctx = fixture.CreateContext())
        {
            created = await NamesAsync(ctx, "index");
        }

        await InitializeAsync(fixture, runs: 2);

        await using (var ctx = fixture.CreateContext())
        {
            (await NamesAsync(ctx, "index")).Where(n => created.Contains(n) || IsModelTable(ctx, n))
                .Should().BeEquivalentTo(created);
        }
    }

    private static bool IsModelTable(MedReminderDbContext ctx, string index)
        => ModelIndexes.Of(ctx.Model).Any(i => index.StartsWith("IX_" + i.Table + "_", StringComparison.Ordinal));

    [Fact]
    public async Task A_database_upgraded_from_the_oldest_schema_matches_a_new_one()
    {
        using var fresh = new SqliteInMemoryFixture();
        await InitializeAsync(fresh, runs: 1);
        var expected = await SchemaAsync(fresh);

        using var upgraded = new SqliteInMemoryFixture();
        await using (var ctx = upgraded.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            foreach (var index in await NamesAsync(ctx, "index"))
            {
                if (!OldestIndexes.Contains(index)) await ctx.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index}\";");
            }
            foreach (var table in await NamesAsync(ctx, "table"))
            {
                if (!OldestTables.Contains(table)) await ctx.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\";");
            }
            foreach (var (table, column) in AddedColumns)
            {
                await ctx.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{table}\" DROP COLUMN \"{column}\";");
            }
        }
        (await SchemaAsync(upgraded)).Tables.Keys.Should().BeEquivalentTo(OldestTables);

        await InitializeAsync(upgraded, runs: 2);

        var actual = await SchemaAsync(upgraded);
        actual.Tables.Should().BeEquivalentTo(expected.Tables);
        actual.Indexes.Should().BeEquivalentTo(expected.Indexes);
    }

    // (MedicineId, Day) covers the reads and the foreign key, so EF adds no
    // (MedicineId) index of its own and the boot patch drops the old one.
    [Fact]
    public async Task The_redundant_intake_medicine_index_is_dropped()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var ctx = fixture.CreateContext())
        {
            ModelIndexes.Of(ctx.Model).Where(i => i.Table == "MedicationIntakes").Select(i => i.Name)
                .Should().BeEquivalentTo("IX_MedicationIntakes_MedicineId_Day", "IX_MedicationIntakes_MedicineId_ScheduledAt");
            await ctx.Database.ExecuteSqlRawAsync(
                @"CREATE INDEX ""IX_MedicationIntakes_MedicineId"" ON ""MedicationIntakes"" (""MedicineId"");");
        }

        await InitializeAsync(fixture, runs: 2);

        await using (var ctx = fixture.CreateContext())
        {
            (await NamesAsync(ctx, "index")).Should().NotContain("IX_MedicationIntakes_MedicineId")
                .And.Contain("IX_MedicationIntakes_MedicineId_Day");
        }
    }

    [Fact]
    public async Task Every_model_index_is_created_once_when_missing()
    {
        using var fixture = new SqliteInMemoryFixture();
        await InitializeAsync(fixture, runs: 1);
        var expected = await SchemaAsync(fixture);
        await using (var ctx = fixture.CreateContext())
        {
            foreach (var index in await NamesAsync(ctx, "index"))
            {
                await ctx.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index}\";");
            }
        }

        await InitializeAsync(fixture, runs: 2);

        (await SchemaAsync(fixture)).Indexes.Should().BeEquivalentTo(expected.Indexes);
        await using (var ctx = fixture.CreateContext())
        {
            var names = await NamesAsync(ctx, "index");
            foreach (var index in ModelIndexes.Of(ctx.Model))
            {
                names.Should().Contain(index.Name);
            }
        }
    }

    private static async Task InitializeAsync(SqliteInMemoryFixture fixture, int runs)
    {
        for (var run = 0; run < runs; run++)
        {
            await using var ctx = fixture.CreateContext();
            await new DatabaseInitializer(ctx, NullLogger<DatabaseInitializer>.Instance)
                .InitializeAsync(CancellationToken.None);
        }
    }

    // The explicit indexes and the tables of the database (SQLite's own
    // autoindexes and tables left out).
    private static async Task<List<string>> NamesAsync(MedReminderDbContext ctx, string type)
        => await ctx.Database
            .SqlQueryRaw<string>(
                $"SELECT name AS \"Value\" FROM sqlite_master WHERE type = '{type}' AND name NOT LIKE 'sqlite_%' AND sql IS NOT NULL")
            .ToListAsync();

    private sealed record Schema(
        Dictionary<string, List<string>> Tables,
        // "table: [UNIQUE] (columns) name" per index, so two equivalent
        // indexes count twice.
        List<string> Indexes);

    private static async Task<Schema> SchemaAsync(SqliteInMemoryFixture fixture)
    {
        await using var ctx = fixture.CreateContext();
        await ctx.Database.OpenConnectionAsync();
        var connection = ctx.Database.GetDbConnection();
        var tables = new Dictionary<string, List<string>>();
        var indexes = new List<string>();
        foreach (var table in await NamesAsync(ctx, "table"))
        {
            tables[table] = [.. (await ctx.Database
                    .SqlQueryRaw<string>($"SELECT name AS \"Value\" FROM pragma_table_info('{table}')")
                    .ToListAsync()).Order()];
            foreach (var index in await ModelIndexes.ListAsync(connection, table, CancellationToken.None))
            {
                if (index.Name.StartsWith("sqlite_", StringComparison.Ordinal)) continue;
                indexes.Add($"{table}: {(index.IsUnique ? "UNIQUE " : "")}({string.Join(", ", index.Columns)}) " +
                            index.Name);
            }
        }
        return new Schema(tables, indexes);
    }
}
