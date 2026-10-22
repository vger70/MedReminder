namespace MedReminder.Application.Abstractions;

// Backup runtime state (kept separate from preferences).
// Persisted to %LOCALAPPDATA%\MedReminder\backup.state.json.
// Not part of IConfiguration: it changes frequently and there is no
// value in observing its diff via reload.
//
// LastSuccessfulBackupAt: the last backup on any target, for the status
// shown in Settings. LastLocalBackupAt / LastCloudBackupAt: the last
// automatic backup on each target, so a target that failed is retried
// the same day while the other one is already done. The store fills them
// when it reads a file written before these keys existed.
//
// LocalError / CloudError: the errors of the last automatic run of each
// target, so a run of one target does not erase the other's. LastError is
// what Settings shows: the automatic errors (AutomaticErrors), or the
// outcome of a manual backup in front of them. LastCloudFailureAt: when
// the cloud target last ran without storing a snapshot for a reason other
// than a sign-in, so a failing target (offline, storage error) is retried
// at most once per CloudRetryInterval instead of on every tick.
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
    DateTimeOffset? LastCloudBackupAt = null,
    string? LocalError = null,
    string? CloudError = null,
    DateTimeOffset? LastCloudFailureAt = null)
{
    public static BackupState Empty { get; } =
        new(LastSuccessfulBackupAt: null, LastAttemptAt: null, LastError: null, LastBackupFile: null);

    // Joins the non-empty parts with "; ", or null when there is none.
    public static string? JoinErrors(params string?[] errors)
    {
        var text = string.Join("; ", errors.Where(e => !string.IsNullOrEmpty(e)));
        return text.Length == 0 ? null : text;
    }

    public string? AutomaticErrors() => JoinErrors(LocalError, CloudError);
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
