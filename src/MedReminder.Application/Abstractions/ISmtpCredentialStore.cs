namespace MedReminder.Application.Abstractions;

// Custodia della password SMTP: mai in chiaro nel repo, mai nei log.
// L'implementazione infrastruttura la cifra con DPAPI CurrentUser e la
// serializza sotto %LOCALAPPDATA%\MedReminder\smtp.protected.
public interface ISmtpCredentialStore
{
    bool HasPassword { get; }

    string? GetPassword();

    void SetPassword(string password);

    void Clear();
}
