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
