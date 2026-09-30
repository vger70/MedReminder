using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryInstallationSettingsStore : IInstallationSettingsStore
{
    public SmtpTransport Smtp { get; set; } = new(string.Empty, 587, true, string.Empty, string.Empty, "MedReminder", 30);
    public BackupSettings Backup { get; set; } = new();
    public UserSettings User { get; set; } = new();

    public SmtpTransport ReadSmtp() => Smtp;
    public void WriteSmtp(SmtpTransport settings) => Smtp = settings;
    public BackupSettings ReadBackup() => Backup;
    public void WriteBackup(BackupSettings settings) => Backup = settings;
    public UserSettings ReadUser() => User;
    public void WriteUser(UserSettings settings) => User = settings;
}

internal sealed class InMemorySmtpCredentialStore : ISmtpCredentialStore
{
    public string? Password { get; set; }
    public bool HasPassword => Password is not null;
    public string? GetPassword() => Password;
    public void SetPassword(string password) => Password = password;
    public void Clear() => Password = null;
}

// Reversible stand-in for DPAPI; a value it did not protect cannot be read.
internal sealed class PrefixCredentialProtector : ICredentialProtector
{
    public string Protect(string plaintext) => "protected:" + plaintext;

    public string Unprotect(string ciphertext)
        => ciphertext.StartsWith("protected:", StringComparison.Ordinal)
            ? ciphertext["protected:".Length..]
            : throw new System.Security.Cryptography.CryptographicException("Not protected on this device.");
}
