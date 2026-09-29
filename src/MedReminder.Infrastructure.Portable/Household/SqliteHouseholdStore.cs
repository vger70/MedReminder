using System.Globalization;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using Microsoft.Data.Sqlite;

namespace MedReminder.Infrastructure.Household;

// IHouseholdStore over <app data>\household\ (household feature, step
// H2; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5.3):
//
//   household.db             operation log and register versions;
//   household.settings.json  household id, device id, generation.
//
// The tables have the shape of SyncOperations and SyncFieldVersions of a
// profile database, so step H3 can reuse the segment pipeline. A fresh
// connection per call, without pooling: the store is small and rarely
// written, and no file handle stays open between calls. The single
// instance of the app (CLAUDE.md §7) is the only writer.
public sealed class SqliteHouseholdStore : IHouseholdStore
{
    public const string DatabaseFileName = "household.db";
    public const string SettingsFileName = "household.settings.json";

    private const string Schema = @"
        CREATE TABLE IF NOT EXISTS ""HouseholdOperations"" (
            ""Id"" TEXT NOT NULL CONSTRAINT ""PK_HouseholdOperations"" PRIMARY KEY,
            ""HlcPhysicalMs"" INTEGER NOT NULL,
            ""HlcCounter"" INTEGER NOT NULL,
            ""DeviceId"" TEXT NOT NULL,
            ""Generation"" INTEGER NOT NULL,
            ""Type"" TEXT NOT NULL,
            ""SchemaVersion"" INTEGER NOT NULL,
            ""ProfileId"" TEXT NOT NULL,
            ""Payload"" TEXT NOT NULL,
            ""SegmentSeq"" INTEGER NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_HouseholdOperations_Hlc""
            ON ""HouseholdOperations"" (""HlcPhysicalMs"", ""HlcCounter"");
        CREATE TABLE IF NOT EXISTS ""HouseholdRegisters"" (
            ""Id"" TEXT NOT NULL CONSTRAINT ""PK_HouseholdRegisters"" PRIMARY KEY,
            ""ProfileId"" TEXT NOT NULL,
            ""Register"" TEXT NOT NULL,
            ""HlcPhysicalMs"" INTEGER NOT NULL,
            ""HlcCounter"" INTEGER NOT NULL,
            ""DeviceId"" TEXT NOT NULL,
            ""Value"" TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ""IX_HouseholdRegisters_ProfileId_Register""
            ON ""HouseholdRegisters"" (""ProfileId"", ""Register"");";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _directory;

    public SqliteHouseholdStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    private string DatabasePath => Path.Combine(_directory, DatabaseFileName);

    private string SettingsPath => Path.Combine(_directory, SettingsFileName);

    public async Task<HouseholdIdentity> EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await using (var connection = await OpenAsync(cancellationToken))
        {
            // Opening created the schema.
        }
        if (File.Exists(SettingsPath))
        {
            var stored = JsonSerializer.Deserialize<HouseholdIdentity>(
                await File.ReadAllTextAsync(SettingsPath, cancellationToken), Json);
            if (stored is not null && stored.HouseholdId != Guid.Empty && stored.DeviceId != Guid.Empty) return stored;
        }
        // A household of one: this device, generation 1.
        var identity = new HouseholdIdentity(Guid.NewGuid(), Guid.NewGuid(), 1);
        var temporary = SettingsPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(identity, Json), cancellationToken);
        File.Move(temporary, SettingsPath, overwrite: true);
        return identity;
    }

    public async Task<HybridTimestamp?> GetLatestTimestampAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""HlcPhysicalMs"", ""HlcCounter"", ""DeviceId"" FROM ""HouseholdOperations""
            ORDER BY ""HlcPhysicalMs"" DESC, ""HlcCounter"" DESC";
        HybridTimestamp? latest = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        // Rows with the same physical time and counter differ by device:
        // the tie-break is the device id, compared in C# (HybridTimestamp).
        while (await reader.ReadAsync(cancellationToken))
        {
            var timestamp = new HybridTimestamp(reader.GetInt64(0), reader.GetInt32(1), Guid.Parse(reader.GetString(2)));
            if (latest is { } known && (timestamp.PhysicalMs != known.PhysicalMs || timestamp.Counter != known.Counter)) break;
            if (latest is null || timestamp > latest.Value) latest = timestamp;
        }
        return latest;
    }

    public async Task AppendAsync(HouseholdOperationRecord operation, IReadOnlyList<HouseholdRegisterVersion> writes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(writes);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = @"INSERT INTO ""HouseholdOperations""
                (""Id"", ""HlcPhysicalMs"", ""HlcCounter"", ""DeviceId"", ""Generation"", ""Type"", ""SchemaVersion"", ""ProfileId"", ""Payload"")
                VALUES ($id, $ms, $counter, $device, $generation, $type, $schema, $profile, $payload)";
            insert.Parameters.AddWithValue("$id", operation.Id.ToString("N"));
            insert.Parameters.AddWithValue("$ms", operation.Timestamp.PhysicalMs);
            insert.Parameters.AddWithValue("$counter", operation.Timestamp.Counter);
            insert.Parameters.AddWithValue("$device", operation.Timestamp.DeviceId.ToString("N"));
            insert.Parameters.AddWithValue("$generation", operation.Generation);
            insert.Parameters.AddWithValue("$type", operation.Type);
            insert.Parameters.AddWithValue("$schema", operation.SchemaVersion);
            insert.Parameters.AddWithValue("$profile", operation.ProfileId);
            insert.Parameters.AddWithValue("$payload", operation.Payload);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var write in writes)
        {
            await using var register = connection.CreateCommand();
            register.Transaction = transaction;
            register.CommandText = @"INSERT INTO ""HouseholdRegisters""
                (""Id"", ""ProfileId"", ""Register"", ""HlcPhysicalMs"", ""HlcCounter"", ""DeviceId"", ""Value"")
                VALUES ($id, $profile, $register, $ms, $counter, $device, $value)";
            register.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            register.Parameters.AddWithValue("$profile", write.ProfileId);
            register.Parameters.AddWithValue("$register", write.Register);
            register.Parameters.AddWithValue("$ms", write.Version.PhysicalMs);
            register.Parameters.AddWithValue("$counter", write.Version.Counter);
            register.Parameters.AddWithValue("$device", write.Version.DeviceId.ToString("N"));
            register.Parameters.AddWithValue("$value", (object?)write.Value ?? DBNull.Value);
            await register.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdRegisterVersion>> ListRegistersAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""ProfileId"", ""Register"", ""HlcPhysicalMs"", ""HlcCounter"", ""DeviceId"", ""Value""
            FROM ""HouseholdRegisters""";
        var versions = new List<HouseholdRegisterVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(new HouseholdRegisterVersion(
                reader.GetString(0),
                reader.GetString(1),
                new HybridTimestamp(reader.GetInt64(2), reader.GetInt32(3), Guid.Parse(reader.GetString(4))),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return versions;
    }

    public async Task<IReadOnlyList<HouseholdOperationRecord>> ListOperationsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Id"", ""HlcPhysicalMs"", ""HlcCounter"", ""DeviceId"", ""Generation"", ""Type"",
            ""SchemaVersion"", ""ProfileId"", ""Payload"" FROM ""HouseholdOperations""";
        var operations = new List<HouseholdOperationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new HouseholdOperationRecord(
                Guid.Parse(reader.GetString(0)),
                new HybridTimestamp(reader.GetInt64(1), reader.GetInt32(2), Guid.Parse(reader.GetString(3))),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8)));
        }
        return [.. operations.OrderBy(o => o.Timestamp)];
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var schema = connection.CreateCommand();
        schema.CommandText = Schema;
        await schema.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }
}
