using MedReminder.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

// Table PendingDataMigrations, kept out of the EF model like the
// catalogue tables: DatabaseInitializer creates it on every boot and
// marks a migration pending when its schema patch upgrades a database;
// ProfileDatabaseBuilder marks every migration pending for an imported
// archive, which may come from an older version.
internal sealed class PendingDataMigrations : IPendingDataMigrations
{
    internal const string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS ""PendingDataMigrations"" (
            ""Name"" TEXT NOT NULL CONSTRAINT ""PK_PendingDataMigrations"" PRIMARY KEY
        );";

    internal const string MarkPendingSql =
        @"INSERT OR IGNORE INTO ""PendingDataMigrations"" (""Name"") VALUES ({0});";

    private readonly MedReminderDbContext _db;

    public PendingDataMigrations(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsPendingAsync(string name, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(CreateTableSql, cancellationToken);
        var count = await _db.Database
            .SqlQueryRaw<int>(@"SELECT COUNT(*) AS ""Value"" FROM ""PendingDataMigrations"" WHERE ""Name"" = {0}", name)
            .SingleAsync(cancellationToken);
        return count > 0;
    }

    public async Task CompleteAsync(string name, CancellationToken cancellationToken)
        => await _db.Database.ExecuteSqlRawAsync(
            @"DELETE FROM ""PendingDataMigrations"" WHERE ""Name"" = {0};", [name], cancellationToken);
}
