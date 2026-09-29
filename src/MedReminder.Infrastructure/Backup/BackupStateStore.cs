using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Backup;

// JSON persistence of BackupState in
// %LOCALAPPDATA%\MedReminder\backup.state.json.
// Tolerant to I/O failures: a corrupted or missing file returns
// BackupState.Empty (the automatic service starts from scratch
// without blocking the app).
internal sealed class BackupStateStore : IBackupStateStore
{
    private const string StateFileName = "backup.state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    // Serialize every access to the file: BackupStateStore is a
    // singleton and is read / written by both the hosted service and
    // the UI.
    private readonly object _sync = new();

    public BackupState Load()
    {
        var path = GetStatePath();
        lock (_sync)
        {
            try
            {
                if (!File.Exists(path)) return BackupState.Empty;
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return BackupState.Empty;
                var state = JsonSerializer.Deserialize<BackupState>(json, JsonOptions);
                return state ?? BackupState.Empty;
            }
            catch
            {
                // File corrupted or unreadable: start from scratch.
                return BackupState.Empty;
            }
        }
    }

    public void Save(BackupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = GetStatePath();
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            // Atomic move: no one sees a half-written file if the
            // process crashes.
            File.Move(tmp, path, overwrite: true);
        }
    }

    private static string GetStatePath() =>
        Path.Combine(AppDataPaths.GetAppDataDirectory(), StateFileName);
}
