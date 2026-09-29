namespace MedReminder.Application.Catalogue;

// Summary returned by a catalogue snapshot import. Values are per
// country / per snapshot.
//
//   Inserted  — rows written for the first time (new national codes).
//   Updated   — rows whose payload changed; unchanged rows are not
//               counted here (importers may treat unchanged rows as
//               either a no-op or an idempotent overwrite — both
//               strategies leave Updated at 0 for those).
//   Deleted   — stale rows for that country removed after the snapshot
//               version bumped.
//   Skipped   — source rows filtered out (Omeopatico, PRINCIPIO_ATTIVO
//               = 'N.D.', missing CODICE_AIC, etc.).
public sealed record ImportReport(
    int Inserted,
    int Updated,
    int Deleted,
    int Skipped,
    string SnapshotVersion,
    DateTimeOffset CompletedAt);
