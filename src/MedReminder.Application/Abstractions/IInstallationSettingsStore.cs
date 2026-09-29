namespace MedReminder.Application.Abstractions;

// The installation-wide settings files (household feature, step H2b):
// smtp.settings.json, backup.settings.json, user.settings.json under
// %LOCALAPPDATA%\MedReminder\. Read returns the effective values (the
// files merged over the defaults of appsettings.json); Write replaces the
// file of that section. The SMTP password stays in ISmtpCredentialStore.
public interface IInstallationSettingsStore
{
    SmtpTransport ReadSmtp();

    void WriteSmtp(SmtpTransport settings);

    BackupSettings ReadBackup();

    void WriteBackup(BackupSettings settings);

    UserSettings ReadUser();

    void WriteUser(UserSettings settings);
}

// The SMTP transport without the password (Settings → Email SMTP).
public sealed record SmtpTransport(
    string Host,
    int Port,
    bool UseStartTls,
    string Username,
    string FromAddress,
    string FromDisplayName,
    int TimeoutSeconds);
