using System.Security.Cryptography;
using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Backup;

// ICloudBackupPassphraseStore for any host (Android backlog B2-03): the
// passphrase of the scheduled cloud backup in cloud-backup.protected,
// protected with the host's credential protector (the Android Keystore
// adapter on the phone). The file holds the protector's output as is; with
// the DPAPI protector that is the format of the desktop's
// DpapiCloudBackupPassphraseStore. Never logged.
//
// ICredentialProtector works on strings, so the passphrase passes through
// one immutable string on each call, which cannot be zeroed; the char[]
// returned to the caller can and must be.
public sealed class ProtectedCloudBackupPassphraseStore : ICloudBackupPassphraseStore
{
    public const string FileName = "cloud-backup.protected";

    private readonly ICredentialProtector _protector;
    private readonly string _path;

    public ProtectedCloudBackupPassphraseStore(ICredentialProtector protector, string directory)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector;
        _path = Path.Combine(directory, FileName);
    }

    public bool HasPassphrase => File.Exists(_path) && new FileInfo(_path).Length > 0;

    // Null when no passphrase is stored or the protector cannot read it
    // (another device's key, a reset Keystore, a damaged file); a
    // protector reports that as CryptographicException or FormatException.
    public char[]? GetPassphrase()
    {
        if (!HasPassphrase) return null;
        var text = File.ReadAllText(_path).Trim();
        if (text.Length == 0) return null;
        try
        {
            var passphrase = _protector.Unprotect(text);
            return passphrase.Length == 0 ? null : passphrase.ToCharArray();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public void SetPassphrase(char[] passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length == 0 || passphrase.All(char.IsWhiteSpace))
        {
            throw new ArgumentException("The backup passphrase must not be empty.", nameof(passphrase));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, _protector.Protect(new string(passphrase)));
        File.Move(temporary, _path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
