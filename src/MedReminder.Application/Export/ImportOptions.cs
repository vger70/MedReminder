namespace MedReminder.Application.Export;

// Caller-chosen switches for a single import run
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.2). The first cut is
// Overwrite-only (§1.3); no merge switch exists by design.
//
// The shared-file restore flags mirror the export opt-ins: a shared
// section is restored only when the archive carried it AND the user
// asked to apply it. Defaults to true so the common "restore
// everything the archive contains" path needs no extra clicks; the UI
// may expose finer control later.
public sealed record ImportOptions
{
    // Restore smtp.settings.json when present in the archive.
    public bool RestoreSmtpSettings { get; init; } = true;

    // Restore the SMTP password when present: AES-GCM-decrypt with the
    // archive key, then DPAPI-re-encrypt into smtp.protected on the
    // current Windows account (§4.2 step 10).
    public bool RestoreSmtpPassword { get; init; } = true;

    // Restore backup.settings.json when present.
    public bool RestoreBackupSettings { get; init; } = true;

    // Restore user.settings.json when present.
    public bool RestoreUserSettings { get; init; } = true;
}
