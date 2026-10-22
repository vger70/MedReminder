namespace MedReminder.Application.Abstractions;

// Backup runtime state (kept separate from preferences).
// Persisted to %LOCALAPPDATA%\MedReminder\backup.state.json.
// Not part of IConfiguration: it changes frequently and there is no
// value in observing its diff via reload.
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
    DateOnly? FailureNotifiedOn = null)
{
    public static BackupState Empty { get; } =
        new(LastSuccessfulBackupAt: null, LastAttemptAt: null, LastError: null, LastBackupFile: null);
}

public interface IBackupStateStore
{
    BackupState Load();
    void Save(BackupState state);
}
