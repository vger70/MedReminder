using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Domain.Household;

namespace MedReminder.Application.UseCases;

// Installation settings through the household (household feature, step
// H2b; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.3). Each use
// case checks who may act, writes the settings file as the Settings
// dialog did, then records in the household every installation setting
// whose value differs from what the household holds.

// Settings → Email SMTP. Admin only.
public sealed class UpdateSmtpSettings
{
    private readonly IInstallationSettingsStore _settings;
    private readonly ISmtpCredentialStore _credentials;
    private readonly ICredentialProtector _protector;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public UpdateSmtpSettings(IInstallationSettingsStore settings, ISmtpCredentialStore credentials,
        ICredentialProtector protector, ICurrentProfile current, HouseholdLog household)
    {
        _settings = settings;
        _credentials = credentials;
        _protector = protector;
        _current = current;
        _household = household;
    }

    // newPassword: null keeps the stored password; clearPassword removes it
    // and wins over newPassword.
    public async Task ExecuteAsync(SmtpTransport transport, string? newPassword, bool clearPassword,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ProfileAdministration.RequireAdmin(_current);

        if (clearPassword) _credentials.Clear();
        else if (!string.IsNullOrEmpty(newPassword)) _credentials.SetPassword(newPassword);
        _settings.WriteSmtp(transport);

        var held = await _household.SettingsAsync(cancellationToken);
        List<HouseholdOperationBody> changes = [.. InstallationSettingsMap.Changes(InstallationSettingsMap.Of(transport), held)];
        if (clearPassword || !string.IsNullOrEmpty(newPassword))
        {
            changes.Add(new HouseholdSettingChanged(HouseholdSetting.SmtpPassword,
                clearPassword ? null : _protector.Protect(newPassword!)));
        }
        await _household.AppendAsync(changes, cancellationToken);
    }
}

// Settings → Backup (automatic backup and scheduled cloud backup). Admin
// only. The whole file is written; only the cloud policy is recorded.
public sealed class UpdateBackupSettings
{
    private readonly IInstallationSettingsStore _settings;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public UpdateBackupSettings(IInstallationSettingsStore settings, ICurrentProfile current, HouseholdLog household)
    {
        _settings = settings;
        _current = current;
        _household = household;
    }

    public async Task ExecuteAsync(BackupSettings backup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ProfileAdministration.RequireAdmin(_current);
        _settings.WriteBackup(backup);
        var held = await _household.SettingsAsync(cancellationToken);
        await _household.AppendAsync([.. InstallationSettingsMap.Changes(InstallationSettingsMap.Of(backup), held)],
            cancellationToken);
    }
}

// Settings → General. Language and update check belong to the device and
// to every profile, as before; the reference country is an installation
// setting, changed by an admin only (decision D-14). The query log is a
// device setting, changed by an admin only.
public sealed class UpdateGeneralSettings
{
    private readonly IInstallationSettingsStore _settings;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public UpdateGeneralSettings(IInstallationSettingsStore settings, ICurrentProfile current, HouseholdLog household)
    {
        _settings = settings;
        _current = current;
        _household = household;
    }

    public async Task ExecuteAsync(UserSettings user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var before = _settings.ReadUser();
        if (!string.Equals(before.ReferenceCountry, user.ReferenceCountry, StringComparison.Ordinal)
            || before.LogDatabaseQueries != user.LogDatabaseQueries)
        {
            ProfileAdministration.RequireAdmin(_current);
        }
        _settings.WriteUser(user);
        var held = await _household.SettingsAsync(cancellationToken);
        await _household.AppendAsync([.. InstallationSettingsMap.Changes(InstallationSettingsMap.Of(user), held)],
            cancellationToken);
    }
}
