using System.Text.Json;
using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// Reads and writes the shared / per-profile JSON settings files that
// the export can opt into (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §3.4). The on-disk files use a single-section wrapper written by
// SettingsDialog, e.g. { "Smtp": { ... } } / { "Backup": { ... } } /
// { "UI": { ... } } / { "Notifications": { ... } }; this reader
// extracts the section and the writer reproduces the same wrapper so a
// restored file loads unchanged through IConfiguration.
//
// The base directory is injectable so tests can point at a temp folder
// instead of %LOCALAPPDATA% (mirrors SmtpCredentialStore's internal
// overload), keeping the real shared settings untouched.
internal sealed class ExportSettingsFiles
{
    private readonly string _sharedDirectory;

    public ExportSettingsFiles(string sharedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharedDirectory);
        _sharedDirectory = sharedDirectory;
    }

    public string SmtpSettingsPath => Path.Combine(_sharedDirectory, "smtp.settings.json");
    public string BackupSettingsPath => Path.Combine(_sharedDirectory, "backup.settings.json");
    public string UserSettingsPath => Path.Combine(_sharedDirectory, "user.settings.json");

    public ExportedSmtpSettings? ReadSmtpSettings()
        => ReadSection<ExportedSmtpSettings>(SmtpSettingsPath, "Smtp");

    public ExportedBackupSettings? ReadBackupSettings()
        => ReadSection<ExportedBackupSettings>(BackupSettingsPath, "Backup");

    public ExportedUserSettings? ReadUserSettings()
        => ReadSection<ExportedUserSettings>(UserSettingsPath, "UI");

    public ExportedNotificationSettings? ReadNotificationSettings(string notificationSettingsPath)
        => ReadSection<ExportedNotificationSettings>(notificationSettingsPath, "Notifications");

    public void WriteSmtpSettings(ExportedSmtpSettings settings)
        => WriteSection(SmtpSettingsPath, "Smtp", settings);

    public void WriteBackupSettings(ExportedBackupSettings settings)
        => WriteSection(BackupSettingsPath, "Backup", settings);

    public void WriteUserSettings(ExportedUserSettings settings)
        => WriteSection(UserSettingsPath, "UI", settings);

    private static TSection? ReadSection<TSection>(string path, string sectionName)
        where TSection : class
    {
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty(sectionName, out var section))
        {
            return null;
        }

        // Section binding is case-insensitive so a hand-written or
        // pre-existing file with slightly different casing still loads.
        return section.Deserialize<TSection>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }

    private static void WriteSection<TSection>(
        string path, string sectionName, TSection settings)
    {
        // Reproduce the { "<Section>": { ... } } wrapper with the same
        // PascalCase section key and indentation SettingsDialog uses, so
        // the restored file is byte-compatible with the app's writer.
        var payload = new Dictionary<string, TSection> { [sectionName] = settings };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }
}
