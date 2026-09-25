namespace MedReminder.Application.Export;

// Caller-chosen switches for a single export run
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.4, §5.2). Every
// "shared settings" flag is opt-in and defaults to false; the profile
// DB itself is always exported.
public sealed record ExportOptions
{
    // Absolute path of the .mrz archive to write. The UI supplies this
    // from a SaveFileDialog.
    public required string DestinationPath { get; init; }

    // Profile to export. Null (default) means the active profile.
    // Another profile may be exported only by an admin profile or by
    // the automatic cloud-folder backup (AutomaticSource), which covers
    // every profile like the local raw-DB backup does. Each archive
    // still holds exactly one profile (scope "profile").
    public string? ProfileId { get; init; }

    // Export scope (§3.5). The first cut ships "profile" only; the
    // admin-only "all-profiles" variant is deferred (§12 item 3).
    public ExportScope Scope { get; init; } = ExportScope.Profile;

    // Include smtp.settings.json (host / port / username / from
    // address — no password). Opt-in (§3.4).
    public bool IncludeSmtpSettings { get; init; }

    // Include the SMTP password. Off by default and always shown with
    // an explicit warning: the password is DPAPI-decrypted on export
    // and re-encrypted with the archive key, never emitted in the
    // clear (§3.4). Ignored unless IncludeSmtpSettings is also set.
    public bool IncludeSmtpPassword { get; init; }

    // Include backup.settings.json (§3.4). Opt-in.
    public bool IncludeBackupSettings { get; init; }

    // Include user.settings.json — UI language + reference-catalogue
    // country (§3.4). Opt-in.
    public bool IncludeUserSettings { get; init; }

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.6, §4.1):
    // marks the manifest as coming from the automatic scheduled cloud
    // target instead of a user-triggered C.3 export. Defaults to false
    // so a regular UI export never annotates itself as automatic.
    public bool AutomaticSource { get; init; }
}

// Export scope discriminator (§3.5). Serialized as the lowercase
// hyphenated token documented in docs/EXPORT-FORMAT.md.
public enum ExportScope
{
    // Current profile only. Default and the only scope shipped in the
    // first cut.
    Profile,

    // Every profile plus the registry. Admin-only; deferred (§12
    // item 3). Present in the enum so the manifest token is stable.
    AllProfiles,
}
