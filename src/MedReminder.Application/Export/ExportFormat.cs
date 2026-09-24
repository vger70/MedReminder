namespace MedReminder.Application.Export;

// Central constants that pin the public archive contract documented in
// docs/EXPORT-FORMAT.md (C.3, docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md
// §3, §7). Everything here is part of the shipped format: changing a
// value is a format-version bump, not a private refactor.
public static class ExportFormat
{
    // Magic string carried in manifest.json so a reader can tell a
    // MedReminder archive from an arbitrary ZIP without decrypting
    // anything (§3.1).
    public const string FormatIdentifier = "medreminder-export";

    // Envelope version (manifest.formatVersion). Tracks the archive
    // container layout — the ZIP entries, the manifest schema, the
    // KDF / cipher wiring (§3.2). Additive: a reader of version N
    // accepts archives of versions <= N (§4.3).
    public const int CurrentFormatVersion = 1;

    // Entity-model version (payload.schemaVersion). Tracks the shape of
    // payload.json (§3.2). Moves independently of FormatVersion and
    // follows the additive SQLite patch discipline (ANALYSIS.md §2.8).
    public const int CurrentSchemaVersion = 1;

    // Minimum passphrase length accepted at export time (§4.5, §12
    // item 7). No composition rules — the Argon2id cost dominates.
    public const int MinPassphraseLength = 12;

    // Default archive extension (§3.1, §12 item 5). A MedReminder-
    // specific extension aids OS-level file association and helps the
    // user recognise the file.
    public const string ArchiveExtension = ".mrz";

    // Fixed ZIP entry names (§3.1). manifest.json is cleartext;
    // payload.enc is the AES-GCM ciphertext; the nonce and tag travel
    // in the manifest's cipher block, so the ZIP holds exactly these
    // two entries. Anything else in the archive is ignored on import
    // (§10 — ZIP-slip mitigation).
    public const string ManifestEntryName = "manifest.json";
    public const string PayloadEntryName = "payload.enc";

    // AES-GCM tag length in bytes (128-bit tag, §3.3).
    public const int AesGcmTagSizeBytes = 16;

    // AES-GCM nonce length in bytes (96-bit nonce, §3.3).
    public const int AesGcmNonceSizeBytes = 12;

    // Derived-key / AES-256 key length in bytes (§3.3).
    public const int KeySizeBytes = 32;

    // Argon2id salt length in bytes.
    public const int SaltSizeBytes = 16;
}
