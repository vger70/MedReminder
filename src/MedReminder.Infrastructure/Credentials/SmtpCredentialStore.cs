using System.Runtime.Versioning;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Credentials;

// File-backed store per la password SMTP: contenuto = base64 di un blob
// DPAPI. Il file (smtp.protected) risiede sotto
// %LOCALAPPDATA%\MedReminder\, mai nel repository e mai in
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

    // Overload interno usato dai test: consente di puntare a un file
    // temporaneo invece che a %LOCALAPPDATA%.
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
