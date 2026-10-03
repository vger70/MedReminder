namespace MedReminder.Application.Abstractions;

// One-time data migrations of a profile that need Application logic
// (facts and sync operations) rather than SQL. The schema patch that
// introduces a migration marks it pending on a database it upgrades;
// the Application runs it and marks it done. A fresh database has
// nothing pending.
public interface IPendingDataMigrations
{
    Task<bool> IsPendingAsync(string name, CancellationToken cancellationToken);

    // Takes effect at once, outside the unit of work: call it after the
    // migration's writes are saved. A failure in between runs the
    // migration again, so a migration must be idempotent.
    Task CompleteAsync(string name, CancellationToken cancellationToken);
}
