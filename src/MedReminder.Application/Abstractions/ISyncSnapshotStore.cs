namespace MedReminder.Application.Abstractions;

// Database images for the genesis and checkpoints (B.1 Phase 3c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.5, §5.6). The image is the
// profile database without what is not replicated: derived stock rows,
// notification and dose-reminder events, the conflict list, the peer
// table and the reference catalogue. Implemented over SQLite in
// Infrastructure.Portable.
public interface ISyncSnapshotStore
{
    // Schema of the images this build writes. An image with a higher
    // value comes from a newer app and is refused (R7).
    int SchemaVersion { get; }

    // Image of the current database. Callers hold WriteGate.
    Task<byte[]> CaptureAsync(CancellationToken cancellationToken);

    // Writes a new database file at `databasePath` from an image, brings
    // it to the current schema, and sets its applied vector. The caller
    // swaps it into place (the process owns one database per profile,
    // CLAUDE.md §7).
    Task BuildDatabaseAsync(
        string databasePath,
        byte[] image,
        int generation,
        IReadOnlyDictionary<Guid, int> appliedVector,
        CancellationToken cancellationToken);
}
