using MedReminder.DataImporter.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MedReminder.DataImporter.Database;

/// <summary>Exports PostgreSQL pharmaceutical master data to an independent SQLite catalog.</summary>
public sealed class SqliteExporter(string postgreSqlConnectionString, ImporterOptions options, ILogger<SqliteExporter> logger)
{
    private readonly string _postgreSqlConnectionString = postgreSqlConnectionString;
    private readonly int _commandTimeout = options.CommandTimeoutSeconds;

    public async Task ExportAsync(string outputPath, bool overwrite, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("--output is required.");
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) && !overwrite) throw new ArgumentException($"SQLite catalog already exists: '{fullPath}'. Use --overwrite to replace it.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        await using var source = new NpgsqlConnection(_postgreSqlConnectionString);
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection($"Data Source={fullPath};Mode=ReadWriteCreate");
        await destination.OpenAsync(cancellationToken);
        await using var transaction = destination.BeginTransaction();
        try
        {
            await CreateSchemaAsync(destination, transaction, cancellationToken);
            await ExecuteAsync(destination, transaction, "PRAGMA defer_foreign_keys = ON;", cancellationToken);
            await CopyAsync(source, destination, transaction, "organisation", "SELECT id, name, country_code, is_active, created_at, updated_at FROM core.organisation", ["id", "name", "country_code", "is_active", "created_at", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "medicinal_product", "SELECT id, source_code, name, organisation_id, is_active, created_at, updated_at FROM core.medicinal_product", ["id", "source_code", "name", "organisation_id", "is_active", "created_at", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "atc", "SELECT code, parent_code, level, description, is_active, updated_at FROM core.atc", ["code", "parent_code", "level", "description", "is_active", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "pharmaceutical_form", "SELECT id, source_code, name, is_active, updated_at FROM core.pharmaceutical_form", ["id", "source_code", "name", "is_active", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "supply_classification", "SELECT id, source_code, name, is_active, updated_at FROM core.supply_classification", ["id", "source_code", "name", "is_active", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "authorization_procedure_type", "SELECT id, source_code, name, is_active, updated_at FROM core.authorization_procedure_type", ["id", "source_code", "name", "is_active", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "package", "SELECT id, medicinal_product_id, aic, package_code, description, pharmaceutical_form_id, supply_classification_id, authorization_procedure_type_id, administrative_status, is_active, link_fi, link_rcp, created_at, updated_at FROM core.package", ["id", "medicinal_product_id", "aic", "package_code", "description", "pharmaceutical_form_id", "supply_classification_id", "authorization_procedure_type_id", "administrative_status", "is_active", "link_fi", "link_rcp", "created_at", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "substance", "SELECT id, preferred_name, substance_type, is_active, created_at, updated_at FROM core.substance", ["id", "preferred_name", "substance_type", "is_active", "created_at", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "package_atc", "SELECT package_id, atc_code FROM core.package_atc", ["package_id", "atc_code"], cancellationToken);
            await CopyAsync(source, destination, transaction, "package_ingredient", "SELECT id, package_id, substance_id, substance_name_source, strength_value, strength_unit, strength_raw, role, ingredient_status, created_at, updated_at FROM core.package_ingredient", ["id", "package_id", "substance_id", "substance_name_source", "strength_value", "strength_unit", "strength_raw", "role", "ingredient_status", "created_at", "updated_at"], cancellationToken);
            await CopyAsync(source, destination, transaction, "document", "SELECT id, package_id, document_type, url, language_code, updated_at FROM core.document", ["id", "package_id", "document_type", "url", "language_code", "updated_at"], cancellationToken);
            await ExecuteAsync(destination, transaction, "PRAGMA user_version = 1;", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("SQLite pharmaceutical catalog exported to {OutputPath}.", fullPath);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
    }

    private async Task CopyAsync(NpgsqlConnection source, SqliteConnection destination, SqliteTransaction transaction, string table, string selectSql, IReadOnlyList<string> columns, CancellationToken cancellationToken)
    {
        await using var sourceCommand = new NpgsqlCommand(selectSql, source) { CommandTimeout = _commandTimeout };
        await using var reader = await sourceCommand.ExecuteReaderAsync(cancellationToken);
        var parameterNames = columns.Select(column => $"@{column}").ToArray();
        await using var destinationCommand = new SqliteCommand($"INSERT INTO [{table}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameterNames)});", destination, transaction);
        foreach (var column in columns) destinationCommand.Parameters.Add(new SqliteParameter($"@{column}", DBNull.Value));

        long rows = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var index = 0; index < columns.Count; index++) destinationCommand.Parameters[index].Value = reader.IsDBNull(index) ? DBNull.Value : reader.GetValue(index);
            await destinationCommand.ExecuteNonQueryAsync(cancellationToken);
            rows++;
        }
        logger.LogInformation("Exported {Rows} {Table} rows.", rows, table);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        const string schema = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE catalog_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO catalog_metadata VALUES ('schema_version', '1'), ('catalog_kind', 'AIFA pharmaceutical master data'), ('source_database', 'PostgreSQL');
            CREATE TABLE organisation (id INTEGER PRIMARY KEY, name TEXT NOT NULL, country_code TEXT, is_active INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE medicinal_product (id INTEGER PRIMARY KEY, source_code TEXT, name TEXT NOT NULL, organisation_id INTEGER, is_active INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY (organisation_id) REFERENCES organisation(id));
            CREATE TABLE atc (code TEXT PRIMARY KEY, parent_code TEXT, level INTEGER NOT NULL, description TEXT NOT NULL, is_active INTEGER NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY (parent_code) REFERENCES atc(code));
            CREATE TABLE pharmaceutical_form (id INTEGER PRIMARY KEY, source_code TEXT, name TEXT NOT NULL, is_active INTEGER NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE supply_classification (id INTEGER PRIMARY KEY, source_code TEXT, name TEXT NOT NULL, is_active INTEGER NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE authorization_procedure_type (id INTEGER PRIMARY KEY, source_code TEXT, name TEXT NOT NULL, is_active INTEGER NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE package (id INTEGER PRIMARY KEY, medicinal_product_id INTEGER NOT NULL, aic TEXT UNIQUE, package_code TEXT, description TEXT, pharmaceutical_form_id INTEGER, supply_classification_id INTEGER, authorization_procedure_type_id INTEGER, administrative_status TEXT, is_active INTEGER NOT NULL, link_fi TEXT, link_rcp TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY (medicinal_product_id) REFERENCES medicinal_product(id), FOREIGN KEY (pharmaceutical_form_id) REFERENCES pharmaceutical_form(id), FOREIGN KEY (supply_classification_id) REFERENCES supply_classification(id), FOREIGN KEY (authorization_procedure_type_id) REFERENCES authorization_procedure_type(id));
            CREATE TABLE substance (id INTEGER PRIMARY KEY, preferred_name TEXT NOT NULL, substance_type TEXT NOT NULL, is_active INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE package_atc (package_id INTEGER NOT NULL, atc_code TEXT NOT NULL, PRIMARY KEY (package_id, atc_code), FOREIGN KEY (package_id) REFERENCES package(id), FOREIGN KEY (atc_code) REFERENCES atc(code));
            CREATE TABLE package_ingredient (id INTEGER PRIMARY KEY, package_id INTEGER NOT NULL, substance_id INTEGER, substance_name_source TEXT, strength_value NUMERIC, strength_unit TEXT, strength_raw TEXT, role TEXT NOT NULL, ingredient_status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, FOREIGN KEY (package_id) REFERENCES package(id), FOREIGN KEY (substance_id) REFERENCES substance(id));
            CREATE TABLE document (id INTEGER PRIMARY KEY, package_id INTEGER NOT NULL, document_type TEXT NOT NULL, url TEXT NOT NULL, language_code TEXT, updated_at TEXT NOT NULL, FOREIGN KEY (package_id) REFERENCES package(id));
            CREATE INDEX ix_package_aic ON package(aic);
            CREATE INDEX ix_package_product ON package(medicinal_product_id);
            CREATE INDEX ix_package_atc_atc ON package_atc(atc_code);
            CREATE INDEX ix_package_ingredient_package ON package_ingredient(package_id);
            """;
        await ExecuteAsync(connection, transaction, schema, cancellationToken);
    }
}
