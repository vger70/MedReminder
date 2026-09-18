using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// End-to-end test for the M1 catalogue import pipeline: the AIFA
// parser feeds the CsvReferenceCatalogueImporter which upserts into
// the three catalogue tables in a single transaction.
public sealed class CsvReferenceCatalogueImporterTests
{
    private static readonly CountryCode Italy = CountryCode.Parse("IT");

    [Fact]
    public async Task Happy_import_writes_the_expected_number_of_rows()
    {
        using var fixture = new SqliteInMemoryFixture();
        var importer = BuildImporter(fixture);

        await using var snapshot = CatalogueFixtures.BuildAifaSnapshotStream();
        var report = await importer.ImportAsync(snapshot, Italy, "202609", CancellationToken.None);

        report.Inserted.Should().Be(168);
        report.Deleted.Should().Be(0);
        report.Skipped.Should().Be(29);
        report.SnapshotVersion.Should().Be("202609");

        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM reference_medicines;")).Should().Be(168);
        (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM reference_active_ingredients;")).Should().BeGreaterThan(0);
        (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM reference_medicine_ingredients;")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Reimporting_the_same_snapshot_version_is_a_noop()
    {
        using var fixture = new SqliteInMemoryFixture();
        var importer = BuildImporter(fixture);

        await using (var first = CatalogueFixtures.BuildAifaSnapshotStream())
        {
            await importer.ImportAsync(first, Italy, "202609", CancellationToken.None);
        }

        await using var second = CatalogueFixtures.BuildAifaSnapshotStream();
        var replay = await importer.ImportAsync(second, Italy, "202609", CancellationToken.None);

        replay.Inserted.Should().Be(0);
        replay.Updated.Should().Be(0);
        replay.Deleted.Should().Be(0);
        replay.Skipped.Should().Be(0);

        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        (await ScalarLongAsync(connection, "SELECT COUNT(*) FROM reference_medicines;")).Should().Be(168);
    }

    [Fact]
    public async Task Importing_a_newer_snapshot_version_replaces_stale_rows()
    {
        using var fixture = new SqliteInMemoryFixture();
        var importer = BuildImporter(fixture);

        await using (var first = CatalogueFixtures.BuildAifaSnapshotStream())
        {
            await importer.ImportAsync(first, Italy, "202609", CancellationToken.None);
        }

        await using var second = CatalogueFixtures.BuildAifaSnapshotStream();
        var upgrade = await importer.ImportAsync(second, Italy, "202610", CancellationToken.None);

        upgrade.Inserted.Should().Be(168);
        upgrade.Deleted.Should().Be(168);
        upgrade.SnapshotVersion.Should().Be("202610");

        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        (await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM reference_medicines WHERE snapshot_version = '202609';"))
            .Should().Be(0);
        (await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM reference_medicines WHERE snapshot_version = '202610';"))
            .Should().Be(168);
    }

    [Fact]
    public async Task Multi_ingredient_medicine_stores_expected_active_ingredients_via_M2M()
    {
        using var fixture = new SqliteInMemoryFixture();
        var importer = BuildImporter(fixture);
        await using (var snapshot = CatalogueFixtures.BuildAifaSnapshotStream())
        {
            await importer.ImportAsync(snapshot, Italy, "202609", CancellationToken.None);
        }

        // Aspirina 500 mg (AIC 004763025) → ACIDO ACETILSALICILICO + ACIDO ASCORBICO.
        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT i.name
              FROM reference_medicine_ingredients mi
              JOIN reference_medicines m ON m.id = mi.medicine_id
              JOIN reference_active_ingredients i ON i.id = mi.ingredient_id
             WHERE m.country = 'IT' AND m.national_code = '004763025'
             ORDER BY i.name;";
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        names.Should().BeEquivalentTo(new[] { "ACIDO ACETILSALICILICO", "ACIDO ASCORBICO" });
    }

    [Fact]
    public async Task Import_dispatches_only_on_supported_country()
    {
        using var fixture = new SqliteInMemoryFixture();
        var importer = BuildImporter(fixture);

        await using var snapshot = CatalogueFixtures.BuildAifaSnapshotStream();
        var act = () => importer.ImportAsync(
            snapshot, CountryCode.Parse("EU"), "202609", CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    private static CsvReferenceCatalogueImporter BuildImporter(SqliteInMemoryFixture fixture)
    {
        return new CsvReferenceCatalogueImporter(
            fixture.CreateContext(),
            new IReferenceSnapshotParser[] { new AifaSnapshotParser() },
            TimeProvider.System);
    }

    private static async Task<long> ScalarLongAsync(
        System.Data.Common.DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0);
    }
}
