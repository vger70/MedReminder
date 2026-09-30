using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Settings;

// IInstallationSettingsStore over the shared settings files (household
// step H2b). Reads the effective values through the options the host
// already binds (the files over appsettings.json); writes the file of a
// section in the shape the Settings dialog wrote before, { "<Section>":
// { ... } }, through a temporary file, so the configuration reload sees a
// complete file. The directory is %LOCALAPPDATA%\MedReminder\
// (IAppDataLocation); nothing is written elsewhere (CLAUDE.md §5).
internal sealed class InstallationSettingsStore : IInstallationSettingsStore
{
    public const string SmtpFileName = "smtp.settings.json";
    public const string BackupFileName = "backup.settings.json";
    public const string UserFileName = "user.settings.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IOptionsMonitor<SmtpSettings> _smtp;
    private readonly IOptionsMonitor<BackupSettings> _backup;
    private readonly IOptionsMonitor<UserSettings> _user;
    private readonly string _directory;

    public InstallationSettingsStore(IOptionsMonitor<SmtpSettings> smtp, IOptionsMonitor<BackupSettings> backup,
        IOptionsMonitor<UserSettings> user, IAppDataLocation location)
    {
        _smtp = smtp;
        _backup = backup;
        _user = user;
        _directory = location.DataDirectory;
    }

    public SmtpTransport ReadSmtp()
    {
        var s = _smtp.CurrentValue;
        return new SmtpTransport(s.Host, s.Port, s.UseStartTls, s.Username, s.FromAddress, s.FromDisplayName,
            s.TimeoutSeconds);
    }

    public void WriteSmtp(SmtpTransport settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(SmtpFileName, new
        {
            Smtp = new SmtpSettings
            {
                Host = settings.Host,
                Port = settings.Port,
                UseStartTls = settings.UseStartTls,
                Username = settings.Username,
                FromAddress = settings.FromAddress,
                FromDisplayName = settings.FromDisplayName,
                TimeoutSeconds = settings.TimeoutSeconds,
            },
        });
    }

    public BackupSettings ReadBackup() => _backup.CurrentValue;

    public void WriteBackup(BackupSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(BackupFileName, new { Backup = settings });
    }

    public UserSettings ReadUser() => _user.CurrentValue;

    public void WriteUser(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Write(UserFileName, new { UI = settings });
    }

    private void Write(string fileName, object payload)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(payload, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
