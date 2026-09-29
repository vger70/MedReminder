namespace MedReminder.Application.Abstractions;

// Vault for the SMTP password: never plaintext in the repo, never in
// the logs. The Infrastructure implementation encrypts it with DPAPI
// CurrentUser and serializes it under
// %LOCALAPPDATA%\MedReminder\smtp.protected.
public interface ISmtpCredentialStore
{
    bool HasPassword { get; }

    string? GetPassword();

    void SetPassword(string password);

    void Clear();
}
