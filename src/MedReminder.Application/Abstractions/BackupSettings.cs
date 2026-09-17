namespace MedReminder.Application.Abstractions;

// User preferences for the automatic daily backup.
// "Backup" section of IConfiguration; persisted to
// %LOCALAPPDATA%\MedReminder\backup.settings.json and merged on top of
// appsettings.json (reloadOnChange=true → IOptionsMonitor refreshes
// without restarting the app).
//
// PreferredTime is textual ("HH:mm") to survive the IConfiguration
// binder: TimeOnly has no default converter, while a string parsed by
// hand is misconfiguration-proof.
public sealed class BackupSettings
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }

    public string Directory { get; set; } = string.Empty;

    // "HH:mm" format (24h, invariant culture). Empty → defaults to 03:00.
    public string PreferredTime { get; set; } = "03:00";

    // Retention window in days. 0 = unlimited (not recommended).
    public int RetentionDays { get; set; } = 30;
}
