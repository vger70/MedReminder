using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Persistence;

// Every index of the EF model on an upgraded database (issue #328).
//
// EnsureCreated builds the indexes of a new database only: an index
// declared after its table was created exists on new databases and is
// missing on upgraded ones (docs/ANALYSIS.md §8.1). This boot patch reads
// the indexes from the model and creates the missing ones with
// CREATE INDEX IF NOT EXISTS, under the name EF generates, so new and
// upgraded databases converge.
//
// An existing index on the same columns with the same uniqueness (and
// both partial or both not) counts as present whatever its name, so no
// second copy is built. An index under the model's name that differs is
// rebuilt as the model declares it. An index whose table or columns are
// missing is left to the patch that adds them.
internal static class ModelIndexes
{
    internal sealed record Definition(
        string Name, string Table, IReadOnlyList<string> Columns, bool IsUnique, string? Filter);

    internal sealed record Existing(string Name, IReadOnlyList<string> Columns, bool IsUnique, bool IsPartial = false);

    // From the relational model: the runtime model does not keep every
    // index facet, the table mapping does.
    public static IReadOnlyList<Definition> Of(IModel model)
        => [.. model.GetRelationalModel().Tables
            .SelectMany(table => table.Indexes.Select(index => new Definition(
                index.Name, table.Name, [.. index.Columns.Select(c => c.Name)], index.IsUnique, index.Filter)))
            .DistinctBy(d => d.Name)];

    // Creates the missing indexes of the model with the given uniqueness,
    // or every missing index when `unique` is null (non-unique ones first
    // in each table). A unique index is created only when no two rows
    // share its key: a failing CREATE UNIQUE INDEX would stop the database
    // from opening. On duplicates the index is skipped, reported, and
    // tried again on the next boot; no row is changed.
    public static async Task EnsureAsync(
        IModel model, DbConnection connection, bool? unique, ILogger log, CancellationToken cancellationToken)
    {
        // The columns and indexes of each table are read once per table.
        var wanted = Of(model).Where(i => unique is null || i.IsUnique == unique).OrderBy(i => i.IsUnique);
        foreach (var table in wanted.GroupBy(i => i.Table))
        {
            var columns = await ColumnsAsync(connection, table.Key, cancellationToken);
            if (columns.Count == 0) continue;
            var existing = await ListAsync(connection, table.Key, cancellationToken);

            foreach (var index in table)
            {
                if (!index.Columns.All(columns.Contains)) continue;
                if (existing.Any(e => Matches(e, index))) continue;

                if (index.IsUnique && await HasDuplicatesAsync(connection, index, cancellationToken))
                {
                    log.LogWarning(
                        "Unique index {Index} not created: {Table} has rows with the same key.", index.Name, index.Table);
                    continue;
                }

                // SQLite index names are case-insensitive. A stale index
                // under the model's name would make CREATE INDEX IF NOT
                // EXISTS a no-op: it is dropped first.
                if (existing.FirstOrDefault(e => string.Equals(e.Name, index.Name, StringComparison.OrdinalIgnoreCase))
                    is { } stale)
                {
                    await ExecuteAsync(connection, $"DROP INDEX IF EXISTS {Quote(stale.Name)};", cancellationToken);
                    log.LogInformation("Rebuilding index {Index} as the model declares it.", index.Name);
                }

                await ExecuteAsync(connection, CreateSql(index), cancellationToken);
                log.LogInformation("Created missing index {Index}.", index.Name);
            }
        }
    }

    private static bool Matches(Existing existing, Definition index)
        => existing.IsUnique == index.IsUnique
            && existing.IsPartial == (index.Filter is not null)
            && existing.Columns.SequenceEqual(index.Columns, StringComparer.OrdinalIgnoreCase);

    internal static string CreateSql(Definition index)
    {
        var columns = string.Join(", ", index.Columns.Select(Quote));
        var filter = index.Filter is null ? "" : $" WHERE {index.Filter}";
        return $"CREATE {(index.IsUnique ? "UNIQUE " : "")}INDEX IF NOT EXISTS {Quote(index.Name)} " +
               $"ON {Quote(index.Table)} ({columns}){filter};";
    }

    // Rows that share the key of a unique index. NULLs never collide in a
    // SQLite unique index, so rows with a NULL in the key are left out.
    private static async Task<bool> HasDuplicatesAsync(
        DbConnection connection, Definition index, CancellationToken cancellationToken)
    {
        var key = string.Join(", ", index.Columns.Select(Quote));
        var notNull = string.Join(" AND ", index.Columns.Select(c => $"{Quote(c)} IS NOT NULL"));
        var filter = index.Filter is null ? "" : $" AND ({index.Filter})";
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT 1 FROM {Quote(index.Table)} WHERE {notNull}{filter} GROUP BY {key} HAVING COUNT(*) > 1 LIMIT 1;";
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public static async Task<IReadOnlyList<Existing>> ListAsync(
        DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var indexes = new List<(string Name, bool Unique, bool Partial)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({Quote(table)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // seq, name, unique, origin, partial
                indexes.Add((reader.GetString(1), reader.GetInt64(2) == 1, reader.GetInt64(4) == 1));
            }
        }

        var result = new List<Existing>();
        foreach (var (name, isUnique, isPartial) in indexes)
        {
            var columns = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info({Quote(name)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // seqno, cid, name
                columns.Add(reader.IsDBNull(2) ? "" : reader.GetString(2));
            }
            result.Add(new Existing(name, columns, isUnique, isPartial));
        }
        return result;
    }

    private static async Task<HashSet<string>> ColumnsAsync(
        DbConnection connection, string table, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
