namespace MedReminder.Application.Abstractions;

// Stato runtime del backup (separato dalle preferenze).
// Persistito in %LOCALAPPDATA%\MedReminder\backup.state.json.
// Non fa parte di IConfiguration: cambia frequentemente e non è
// interessante osservarne il diff via reload.
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
