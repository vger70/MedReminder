namespace MedReminder.Application.Abstractions;

// Backup runtime state (kept separate from preferences).
// Persisted to %LOCALAPPDATA%\MedReminder\backup.state.json.
// Not part of IConfiguration: it changes frequently and there is no
// value in observing its diff via reload.
//
// LastSuccessfulBackupAt: the last backup on any target, for the status
// shown in Settings. LastLocalBackupAt / LastCloudBackupAt: the last
// automatic backup on each target, so a target that failed is retried
// the same day while the other one is already done. The store fills both
// from LastSuccessfulBackupAt when it reads a file written before these
// two keys existed.
//
// SignInRequired: the cloud account whose session ended on the last
// attempt (disconnected, revoked, expired), kept apart from LastError so
// the UI can show a localized message. FailureNotifiedOn: the local day
// the user was last notified of a failed automatic backup, so the
// 15-minute ticks notify at most once a day. Older files without these
// keys deserialize to null.
public sealed record BackupState(
    DateTimeOffset? LastSuccessfulBackupAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    string? LastBackupFile,
    CloudProvider? SignInRequired = null,
    DateOnly? FailureNotifiedOn = null,
    DateTimeOffset? LastLocalBackupAt = null,
    DateTimeOffset? LastCloudBackupAt = null)
{
    public static BackupState Empty { get; } =
        new(LastSuccessfulBackupAt: null, LastAttemptAt: null, LastError: null, LastBackupFile: null);
}

public interface IBackupStateStore
{
    BackupState Load();

    // Reads the current state, applies change and saves the result in one
    // step. The scheduler and Settings both write the state, and a tick
    // can run for minutes: each writer changes only its own fields on the
    // latest state, so neither undoes the other's change. Returning the
    // same instance saves nothing. Returns the resulting state.
    BackupState Update(Func<BackupState, BackupState> change);
}
