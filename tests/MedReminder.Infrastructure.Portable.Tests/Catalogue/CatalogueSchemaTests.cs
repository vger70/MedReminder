using FluentAssertions;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Sanity-checks the additive DDL step required by ANALYSIS-DRUG-
// CATALOGUE.md §2.4: catalogue tables come into existence via
// CREATE * IF NOT EXISTS, running the step twice never fails, and
// the three optional Medicines columns are wired for query use.
public sealed class CatalogueSchemaTests
{
    [Fact]
    public async Task Applying_the_schema_creates_the_three_catalogue_tables()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();

        await CatalogueSchema.ApplyAsync(connection, CancellationToken.None);

        (await TableExistsAsync(connection, "reference_medicines")).Should().BeTrue();
        (await TableExistsAsync(connection, "reference_active_ingredients")).Should().BeTrue();
        (await TableExistsAsync(connection, "reference_medicine_ingredients")).Should().BeTrue();
    }

    [Fact]
    public async Task Applying_the_schema_twice_is_a_noop()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();

        await CatalogueSchema.ApplyAsync(connection, CancellationToken.None);
        var act = async () => await CatalogueSchema.ApplyAsync(connection, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Foreign_key_cascades_from_reference_medicines_to_the_join_table()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var context = fixture.CreateContext();
        var connection = context.Database.GetDbConnection();
        await CatalogueSchema.ApplyAsync(connection, CancellationToken.None);
        await ExecAsync(connection, "PRAGMA foreign_keys = ON;");

        var medicineId = Guid.NewGuid().ToString();
        var ingredientId = Guid.NewGuid().ToString();

        await ExecAsync(connection,
            "INSERT INTO reference_active_ingredients (id, country, name, name_norm, atc_code) VALUES ($id, 'IT', 'PARACETAMOLO', 'paracetamolo', NULL);",
            ("$id", ingredientId));
        await ExecAsync(connection,
            @"INSERT INTO reference_medicines
                (id, country, national_code, commercial_name, commercial_name_norm, snapshot_version)
              VALUES ($id, 'IT', '000000001', 'TACHIPIRINA', 'tachipirina', '202609');",
            ("$id", medicineId));
        await ExecAsync(connection,
            "INSERT INTO reference_medicine_ingredients (medicine_id, ingredient_id) VALUES ($m, $i);",
            ("$m", medicineId), ("$i", ingredientId));

        await ExecAsync(connection, "DELETE FROM reference_medicines WHERE id = $id;", ("$id", medicineId));

        var join = await ScalarLongAsync(connection,
            "SELECT COUNT(*) FROM reference_medicine_ingredients WHERE medicine_id = $id;",
            ("$id", medicineId));
        join.Should().Be(0);
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task ExecAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarLongAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0);
    }
}
