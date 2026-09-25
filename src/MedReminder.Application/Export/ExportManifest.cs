namespace MedReminder.Application.Export;

// Cleartext metadata written to manifest.json inside the archive
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.1). Inspectable
// without the passphrase; carries no medical data. Serialized with
// System.Text.Json (camelCase). The importer honours what this
// manifest declares — the KDF and cipher blocks make the archive
// self-describing (§4.3).
public sealed class ExportManifest
{
    // Magic string; must equal ExportFormat.FormatIdentifier for the
    // archive to be recognised (§4.3).
    public string Format { get; set; } = ExportFormat.FormatIdentifier;

    // Envelope version (§3.2). Must be <= CurrentFormatVersion to
    // import.
    public int FormatVersion { get; set; } = ExportFormat.CurrentFormatVersion;

    // MedReminder version that produced the archive, for diagnostics.
    public string AppVersion { get; set; } = string.Empty;

    // Export instant, UTC, ISO-8601.
    public DateTimeOffset CreatedAtUtc { get; set; }

    // "profile" or "all-profiles" (§3.5). The first cut always writes
    // "profile".
    public string Scope { get; set; } = "profile";

    // Present when Scope == "profile": the exported profile's id.
    public string? ProfileId { get; set; }

    // Argon2id KDF descriptor (§3.1).
    public ManifestKdf Kdf { get; set; } = new();

    // AES-GCM cipher descriptor (§3.1). Holds the nonce and tag; the
    // ciphertext itself lives in the payload.enc ZIP entry.
    public ManifestCipher Cipher { get; set; } = new();

    // Integrity descriptor over the DECRYPTED payload.json (§3.1).
    public ManifestPayload Payload { get; set; } = new();

    // Which opt-in shared files the archive carries (§3.4).
    public ManifestIncludes Includes { get; set; } = new();

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.6):
    // optional origin marker. "automatic" for a scheduled cloud
    // snapshot, "user" (or null) for a user-triggered C.3 export. Older
    // readers ignore the property (§4.3 additive-field rule).
    public string? Source { get; set; }

    // C.3+ §3.6: optional device descriptor. Present on automatic
    // cloud snapshots; the host name is stored HASHED (SHA-256, hex,
    // lower-case) so a leaked archive does not disclose the plain
    // host name. Enough to group snapshots by originating machine in
    // the Restore dialog, not enough to identify it.
    public ManifestDevice? Device { get; set; }
}

public sealed class ManifestDevice
{
    // SHA-256 hex of the plain host name. Never the plain name.
    public string HostNameSha256 { get; set; } = string.Empty;

    // Convenience mirror of the top-level ProfileId — the Restore
    // dialog reads this from a folder listing without decrypting the
    // payload, so keeping it here saves an extra manifest lookup on
    // the sibling ProfileId field.
    public string ProfileId { get; set; } = string.Empty;
}

public sealed class ManifestKdf
{
    public string Algorithm { get; set; } = "Argon2id";
    public int Iterations { get; set; }
    public int MemoryKiB { get; set; }
    public int Parallelism { get; set; }
    public string SaltBase64 { get; set; } = string.Empty;
}

public sealed class ManifestCipher
{
    public string Algorithm { get; set; } = "AES-GCM";
    public int KeyBits { get; set; } = 256;
    public string NonceBase64 { get; set; } = string.Empty;
    public string TagBase64 { get; set; } = string.Empty;
}

public sealed class ManifestPayload
{
    // Base64 SHA-256 of the decrypted payload.json plaintext (§4.2
    // step 5). A mismatch after a successful decrypt means a corrupt
    // archive.
    public string Sha256Base64 { get; set; } = string.Empty;

    // Size in bytes of the decrypted payload.json plaintext.
    public long SizeBytes { get; set; }
}

public sealed class ManifestIncludes
{
    public bool SmtpCredential { get; set; }
    public bool SmtpSettings { get; set; }
    public bool UserSettings { get; set; }
    public bool BackupSettings { get; set; }
}
