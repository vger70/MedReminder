using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Credentials;

// File-backed store for the SMTP password: contents = base64 of a
// DPAPI blob. The file (smtp.protected) lives under
// %LOCALAPPDATA%\MedReminder\, never in the repository and never in
// appsettings.json (spec §22).
[SupportedOSPlatform("windows")]
internal sealed class SmtpCredentialStore : ISmtpCredentialStore
{
    private readonly ICredentialProtector _protector;
    private readonly string _filePath;

    public SmtpCredentialStore(ICredentialProtector protector)
        : this(protector, filePath: null)
    {
    }

    // Internal overload used by tests: lets us point at a temporary
    // file instead of %LOCALAPPDATA%.
    internal SmtpCredentialStore(ICredentialProtector protector, string? filePath)
    {
        _protector = protector;
        _filePath = filePath ?? AppDataPaths.GetCredentialsPath();
    }

    public bool HasPassword => File.Exists(_filePath) && new FileInfo(_filePath).Length > 0;

    public string? GetPassword()
    {
        if (!HasPassword) return null;
        var ciphertext = File.ReadAllText(_filePath).Trim();
        if (string.IsNullOrEmpty(ciphertext)) return null;
        return _protector.Unprotect(ciphertext);
    }

    public void SetPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var ciphertext = _protector.Protect(password);
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, ciphertext);
    }

    public void Clear()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }
}
