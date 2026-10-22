using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Backup;

// JSON persistence of BackupState in
// %LOCALAPPDATA%\MedReminder\backup.state.json.
// Tolerant to I/O failures: a corrupted or missing file returns
// BackupState.Empty (the automatic service starts from scratch
// without blocking the app).
//
// A file written before the per-target days (LastLocalBackupAt,
// LastCloudBackupAt) has neither key: both take LastSuccessfulBackupAt,
// so the upgrade does not run a second backup on a day already covered.
// A newer file writes the keys even when null, so a manual backup saved
// before any automatic one does not close the day.
internal sealed class BackupStateStore : IBackupStateStore
{
    private const string StateFileName = "backup.state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    // Serialize every access to the file: BackupStateStore is a
    // singleton and is read / written by both the hosted service and
    // the UI. One process owns the file (single-instance mutex), so a
    // process-wide lock makes Update atomic.
    private readonly object _sync = new();
    private readonly string? _path;

    public BackupStateStore()
    {
    }

    internal BackupStateStore(string path)
    {
        _path = path;
    }

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
                if (state is null) return BackupState.Empty;
                return HasTargetDays(json)
                    ? state
                    : state with
                    {
                        LastLocalBackupAt = state.LastSuccessfulBackupAt,
                        LastCloudBackupAt = state.LastSuccessfulBackupAt,
                    };
            }
            catch
            {
                // File corrupted or unreadable: start from scratch.
                return BackupState.Empty;
            }
        }
    }

    public BackupState Update(Func<BackupState, BackupState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_sync)
        {
            var current = Load();
            var updated = change(current) ?? throw new InvalidOperationException("The change returned no state.");
            if (!ReferenceEquals(updated, current)) Write(updated);
            return updated;
        }
    }

    private static bool HasTargetDays(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(nameof(BackupState.LastLocalBackupAt), out _);
    }

    private void Write(BackupState state)
    {
        var path = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        // Atomic move: no one sees a half-written file if the
        // process crashes.
        File.Move(tmp, path, overwrite: true);
    }

    private string GetStatePath() =>
        _path ?? Path.Combine(AppDataPaths.GetAppDataDirectory(), StateFileName);
}
