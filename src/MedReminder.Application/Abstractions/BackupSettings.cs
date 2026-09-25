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
//
// C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.1) adds three
// backward-compatible fields for the optional encrypted-snapshot target
// that writes into a user-controlled cloud-synced folder. Older files
// missing these keys deserialize to the defaults (cloud target off).
public sealed class BackupSettings
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; }

    public string Directory { get; set; } = string.Empty;

    // "HH:mm" format (24h, invariant culture). Empty → defaults to 03:00.
    public string PreferredTime { get; set; } = "03:00";

    // Retention window in days. 0 = unlimited (not recommended).
    public int RetentionDays { get; set; } = 30;

    // C.3+ §3.1: independent toggle for the encrypted cloud-folder
    // target. May be on with or without the local raw-DB backup above.
    public bool CloudFolderEnabled { get; set; }

    // C.3+ §3.1: second target directory, distinct from Directory. The
    // user picks any local folder — the app never inspects whether it
    // is actually cloud-synced (§4.3). Empty when
    // CloudFolderEnabled is false.
    public string CloudFolderDirectory { get; set; } = string.Empty;

    // C.3+ §3.1: number of encrypted .mrz snapshots kept in the cloud
    // folder. 0 = unlimited (not recommended). Independent from
    // RetentionDays, which governs the local raw-DB backups.
    public int CloudFolderRetention { get; set; } = 30;
}
