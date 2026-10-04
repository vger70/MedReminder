using MedReminder.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

// One row per one-time data migration still to run (table
// PendingDataMigrations, in the EF model so a new database has it). The
// schema patch that introduces a migration marks it on a database it
// upgrades; ProfileDatabaseBuilder marks it for an imported archive
// written before the data it corrects.
internal sealed class PendingDataMigration
{
    public required string Name { get; init; }
}

internal sealed class PendingDataMigrations : IPendingDataMigrations
{
    // For DatabaseInitializer, which patches databases created before
    // the table was in the model.
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

    public Task<bool> IsPendingAsync(string name, CancellationToken cancellationToken)
        => _db.PendingDataMigrations.AsNoTracking().AnyAsync(m => m.Name == name, cancellationToken);

    public async Task CompleteAsync(string name, CancellationToken cancellationToken)
        => await _db.PendingDataMigrations.Where(m => m.Name == name).ExecuteDeleteAsync(cancellationToken);
}
