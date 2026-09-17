namespace MedReminder.Application.Abstractions;

// Backup runtime state (kept separate from preferences).
// Persisted to %LOCALAPPDATA%\MedReminder\backup.state.json.
// Not part of IConfiguration: it changes frequently and there is no
// value in observing its diff via reload.
public sealed record BackupState(
    DateTimeOffset? LastSuccessfulBackupAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    string? LastBackupFile)
{
    public static BackupState Empty { get; } =
        new(LastSuccessfulBackupAt: null, LastAttemptAt: null, LastError: null, LastBackupFile: null);
}

public interface IBackupStateStore
{
    BackupState Load();
    void Save(BackupState state);
}
