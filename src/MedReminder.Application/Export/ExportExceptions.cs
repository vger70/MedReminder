namespace MedReminder.Application.Export;

// Raised by IExportService when the requested export cannot proceed —
// today only a passphrase shorter than
// ExportFormat.MinPassphraseLength (§4.5). Thrown before any file is
// written so a rejected export never leaves a partial archive on disk.
// The UI maps Reason to a localized message.
public sealed class ExportValidationException : Exception
{
    public ExportValidationException(ExportValidationReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public ExportValidationReason Reason { get; }
}

public enum ExportValidationReason
{
    // Passphrase empty or below the minimum length.
    PassphraseTooShort,

    // Scope requested that the current profile is not allowed to use
    // (e.g. all-profiles from a non-admin profile, or another profile's
    // data from a non-admin profile).
    ScopeNotPermitted,

    // ExportOptions.ProfileId does not match any registered profile.
    ProfileNotFound,
}

// Raised by IImportService for every failure surface in §4.4. The UI
// maps Reason to exactly one localized message; the three
// user-distinguishable reasons match the three localization keys.
public sealed class ImportFailedException : Exception
{
    public ImportFailedException(ImportFailureReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public ImportFailedException(
        ImportFailureReason reason, string message, Exception innerException)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public ImportFailureReason Reason { get; }
}

public enum ImportFailureReason
{
    // AES-GCM tag mismatch on decrypt — wrong passphrase or tampered
    // ciphertext. The two are deliberately indistinguishable from the
    // outside (§4.4, documented in docs/EXPORT-FORMAT.md).
    WrongPassphrase,

    // Not a MedReminder archive, truncated ZIP, missing entry, bad
    // manifest, or a payload SHA-256 mismatch after a successful
    // decrypt. Surfaced as "the export file is damaged".
    Corrupt,

    // Archive produced by a newer app: formatVersion or schemaVersion
    // above what this build supports (§4.3).
    UnsupportedVersion,
}
