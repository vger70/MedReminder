namespace MedReminder.Application.Export;

// Port for the archive encryption primitives: an Argon2id KDF and an
// AES-GCM cipher (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §3.3,
// §13 step 2). The concrete adapter lives in Infrastructure
// (ArchiveCipher, backed by Konscious.Security.Cryptography.Argon2 and
// System.Security.Cryptography.AesGcm). Kept in Application so the
// export/import services and their unit tests depend only on the
// abstraction — no Windows-specific or native dependency leaks up.
//
// DPAPI is deliberately NOT part of this port: the archive key is
// derived from the user passphrase so the archive travels across
// Windows accounts and machines (§1.2). DPAPI is used only to rewrap
// the SMTP password on the target machine, and that lives entirely in
// Infrastructure.
//
// Implementations MUST never log the passphrase, the derived key, or
// any plaintext (CLAUDE.md §9), and SHOULD zero transient key material
// after use.
public interface IArchiveCipher
{
    // Derives a symmetric key from the passphrase and salt using the
    // given Argon2id parameters. Deterministic: the same
    // (passphrase, salt, parameters) always yields the same key.
    // Returns a fresh KeySizeBytes-long buffer the caller owns and is
    // responsible for zeroing after use.
    byte[] DeriveKey(char[] passphrase, byte[] salt, Argon2Params parameters);

    // AES-GCM encrypts the plaintext with the given key, generating a
    // fresh random nonce. Returns the nonce, the authentication tag,
    // and the ciphertext as separate buffers.
    (byte[] Nonce, byte[] Tag, byte[] Ciphertext) Encrypt(byte[] key, byte[] plaintext);

    // AES-GCM decrypts the ciphertext with the given key, nonce and
    // tag. Throws System.Security.Cryptography.CryptographicException
    // on a tag mismatch (wrong key or tampered data) — the caller
    // surfaces that as the "wrong passphrase" case (§4.4).
    byte[] Decrypt(byte[] key, byte[] nonce, byte[] tag, byte[] ciphertext);
}
