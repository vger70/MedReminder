using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Credentials;

// File-backed store for the C.3+ automatic-cloud-backup passphrase
// (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.4, §3.5). Mirrors
// SmtpCredentialStore: DPAPI CurrentUser blob, base64-encoded, written
// to %LOCALAPPDATA%\MedReminder\cloud-backup.protected. Never
// re-emitted in the clear, never logged. A separate file (not
// smtp.protected) so the two credential lifecycles stay independent —
// clearing the SMTP password does not erase the backup passphrase and
// vice versa.
[SupportedOSPlatform("windows")]
internal sealed class DpapiCloudBackupPassphraseStore : ICloudBackupPassphraseStore
{
    public const string FileName = "cloud-backup.protected";

    private readonly string _filePath;

    public DpapiCloudBackupPassphraseStore()
        : this(filePath: null)
    {
    }

    // Internal overload used by tests: lets us point at a temporary
    // file instead of %LOCALAPPDATA%.
    internal DpapiCloudBackupPassphraseStore(string? filePath)
    {
        _filePath = filePath ?? Path.Combine(AppDataPaths.GetAppDataDirectory(), FileName);
    }

    public bool HasPassphrase => File.Exists(_filePath) && new FileInfo(_filePath).Length > 0;

    public char[]? GetPassphrase()
    {
        if (!HasPassphrase) return null;

        var text = File.ReadAllText(_filePath).Trim();
        if (string.IsNullOrEmpty(text)) return null;

        byte[] ciphertext;
        try
        {
            ciphertext = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[]? plaintextBytes = null;
        try
        {
            plaintextBytes = ProtectedData.Unprotect(
                ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetChars(plaintextBytes);
        }
        catch (CryptographicException)
        {
            // Blob written by another Windows account or tampered with.
            return null;
        }
        finally
        {
            if (plaintextBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }
    }

    public void SetPassphrase(char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length == 0 || AllWhitespace(passphrase))
        {
            throw new ArgumentException(
                "The backup passphrase must not be empty.", nameof(passphrase));
        }

        byte[]? bytes = null;
        try
        {
            bytes = Encoding.UTF8.GetBytes(passphrase);
            var ciphertext = ProtectedData.Protect(
                bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, Convert.ToBase64String(ciphertext));
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private static bool AllWhitespace(char[] passphrase)
    {
        foreach (var c in passphrase)
        {
            if (!char.IsWhiteSpace(c)) return false;
        }
        return true;
    }
}
