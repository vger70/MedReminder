using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Backup;

// Persistenza JSON di BackupState in
// %LOCALAPPDATA%\MedReminder\backup.state.json.
// Tolerante ai fallimenti di I/O: un file corrotto o assente ritorna
// BackupState.Empty (il servizio automatico ricomincia da zero senza
// bloccare l'app).
internal sealed class BackupStateStore : IBackupStateStore
{
    private const string StateFileName = "backup.state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    // Serializza tutti gli accessi al file: BackupStateStore è singleton
    // e viene letto/scritto sia dall'hosted service che dalla UI.
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
                // File corrotto o non leggibile: partiamo da zero.
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
            // Move atomico: nessuno vede un file mezzo scritto in caso di crash.
            File.Move(tmp, path, overwrite: true);
        }
    }

    private static string GetStatePath() =>
        Path.Combine(AppDataPaths.GetAppDataDirectory(), StateFileName);
}
