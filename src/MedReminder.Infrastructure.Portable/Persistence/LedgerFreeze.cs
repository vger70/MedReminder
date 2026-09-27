using System.Data.Common;
using System.Globalization;
using MedReminder.Domain.Stock;

namespace MedReminder.Infrastructure.Persistence;

// Freezes a profile database for the ledger derivation (B.1 §3.5,
// Phase 2c-2): everything recorded so far stays exactly as it is and
// the derivation only works after the cutoff. Used by the boot patch
// and by the import of an archive (ProfileDatabaseBuilder), which must
// leave the same state.
//
//   - every stock movement becomes Legacy;
//   - each medicine's current StockEpoch becomes its epoch baseline;
//   - cutoff = the local day before `now`, frozen at `now`;
//   - an inactive medicine gets an activity fact "inactive from the day
//     after the cutoff" (D15), reusing the medicine's id.
// Intakes, counts and schedule rows need no update: those recorded
// before `now` read as Legacy through their recording instant.
internal static class LedgerFreeze
{
    public static async Task ApplyAsync(
        DbConnection connection,
        DbTransaction transaction,
        DateTimeOffset now,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var cutoffDay = localToday.AddDays(-1);

        await ExecuteAsync(connection, transaction,
            $@"UPDATE ""StockMovements"" SET ""Origin"" = {(int)StockMovementOrigin.Legacy};", cancellationToken);
        await ExecuteAsync(connection, transaction,
            @"UPDATE ""Medicines"" SET ""LedgerBaselineEpoch"" = ""StockEpoch"";", cancellationToken);
        await ExecuteAsync(connection, transaction, @"
            INSERT OR REPLACE INTO ""LedgerCutoff"" (""Id"", ""CutoffDay"", ""FrozenAt"")
            VALUES ($id, $cutoffDay, $frozenAt);",
            cancellationToken,
            ("$id", LedgerCutoff.SingletonId),
            ("$cutoffDay", Day(cutoffDay)),
            ("$frozenAt", now.UtcTicks));
        await ExecuteAsync(connection, transaction, @"
            INSERT OR IGNORE INTO ""MedicineActivityChanges"" (""Id"", ""MedicineId"", ""Day"", ""Active"", ""RecordedAt"")
            SELECT ""Id"", ""Id"", $day, 0, $recordedAt FROM ""Medicines"" WHERE ""IsActive"" = 0;",
            cancellationToken,
            ("$day", Day(cutoffDay.AddDays(1))),
            ("$recordedAt", now.UtcTicks));
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
