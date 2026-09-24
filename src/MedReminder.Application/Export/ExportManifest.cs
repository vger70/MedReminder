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
