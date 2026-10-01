using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Household;

namespace MedReminder.Application.Household;

// Writes the household's winning values into the files the app reads
// (household step H3a): profiles.json through the registry, and the
// installation settings files through IInstallationSettingsStore and
// ISmtpCredentialStore. Runs after remote operations were applied; a file
// is written only when a value differs.
//
// Profiles: only those this installation already has are updated (name,
// role, PIN). Creating a profile on this device, or deleting one, is a
// join or an admin action (steps H3b–H3d), never a side effect of sync.
public sealed class HouseholdProjection
{
    private readonly HouseholdLog _household;
    private readonly IProfileRegistry _registry;
    private readonly IInstallationSettingsStore? _settings;
    private readonly ISmtpCredentialStore? _credentials;
    private readonly ICredentialProtector? _protector;

    public HouseholdProjection(HouseholdLog household, IProfileRegistry registry,
        IInstallationSettingsStore? settings = null, ISmtpCredentialStore? credentials = null,
        ICredentialProtector? protector = null)
    {
        _household = household;
        _registry = registry;
        _settings = settings;
        _credentials = credentials;
        _protector = protector;
    }

    // Returns the problems found (ids and setting names only).
    public async Task<IReadOnlyList<string>> ProjectAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        ProjectProfiles(await _household.ProfilesAsync(cancellationToken), problems);
        ProjectSettings(await _household.SettingsAsync(cancellationToken), problems);
        return problems;
    }

    private void ProjectProfiles(IReadOnlyList<HouseholdProfile> profiles, List<string> problems)
    {
        foreach (var held in profiles)
        {
            if (_registry.GetById(held.ProfileId) is not { } local) continue;
            if (held.DisplayName.Length > 0 && !string.Equals(local.DisplayName, held.DisplayName, StringComparison.Ordinal))
            {
                _registry.Rename(held.ProfileId, held.DisplayName);
            }
            var role = held.Role == HouseholdRole.Admin ? ProfileRole.Admin : ProfileRole.User;
            if (local.Role != role)
            {
                try
                {
                    _registry.SetRole(held.ProfileId, role);
                }
                catch (InvalidOperationException)
                {
                    // The last admin of this installation stays admin until
                    // another one arrives.
                    problems.Add($"Profile {held.ProfileId} keeps its role: it is the last administrator here.");
                }
            }
            var hash = _registry.GetPinHash(held.ProfileId);
            if (!string.Equals(HouseholdRegisters.PinValue(hash?.Hash, hash?.Salt, hash?.Iterations ?? 0), held.Pin,
                    StringComparison.Ordinal))
            {
                _registry.SetPinHash(held.ProfileId,
                    HouseholdRegisters.TryParsePin(held.Pin, out var h, out var s, out var i) ? new ProfilePinHash(h, s, i) : null);
            }
        }
    }

    private void ProjectSettings(IReadOnlyDictionary<string, string?> held, List<string> problems)
    {
        if (_settings is null || held.Count == 0) return;

        var smtp = _settings.ReadSmtp();
        var smtpWanted = smtp with
        {
            Host = Text(held, HouseholdSetting.SmtpHost, smtp.Host),
            Port = Integer(held, HouseholdSetting.SmtpPort, smtp.Port),
            UseStartTls = Boolean(held, HouseholdSetting.SmtpUseStartTls, smtp.UseStartTls),
            Username = Text(held, HouseholdSetting.SmtpUsername, smtp.Username),
            FromAddress = Text(held, HouseholdSetting.SmtpFromAddress, smtp.FromAddress),
            FromDisplayName = Text(held, HouseholdSetting.SmtpFromDisplayName, smtp.FromDisplayName),
            TimeoutSeconds = Integer(held, HouseholdSetting.SmtpTimeoutSeconds, smtp.TimeoutSeconds),
        };
        if (smtpWanted != smtp) _settings.WriteSmtp(smtpWanted);

        if (_credentials is not null && _protector is not null && held.TryGetValue(HouseholdSetting.SmtpPassword, out var secret))
        {
            string? password;
            try
            {
                password = secret is null ? null : _protector.Unprotect(secret);
            }
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                problems.Add("The SMTP password of the household cannot be read on this device.");
                password = _credentials.GetPassword();
            }
            if (!string.Equals(password, _credentials.GetPassword(), StringComparison.Ordinal))
            {
                if (password is null) _credentials.Clear();
                else _credentials.SetPassword(password);
            }
        }

        var backup = _settings.ReadBackup();
        var enabled = Boolean(held, HouseholdSetting.CloudBackupEnabled, backup.CloudFolderEnabled);
        var retention = Integer(held, HouseholdSetting.CloudBackupRetention, backup.CloudFolderRetention);
        var provider = held.TryGetValue(HouseholdSetting.CloudBackupProvider, out var p)
            ? (Enum.TryParse<CloudProvider>(p, out var parsed) ? parsed : (CloudProvider?)null)
            : backup.CloudProvider;
        var account = Text(held, HouseholdSetting.CloudBackupAccountId, backup.CloudAccountId);
        if (enabled != backup.CloudFolderEnabled || retention != backup.CloudFolderRetention
            || provider != backup.CloudProvider || !string.Equals(account, backup.CloudAccountId, StringComparison.Ordinal))
        {
            _settings.WriteBackup(new BackupSettings
            {
                Enabled = backup.Enabled,
                Directory = backup.Directory,
                PreferredTime = backup.PreferredTime,
                RetentionDays = backup.RetentionDays,
                CloudFolderEnabled = enabled,
                CloudFolderDirectory = backup.CloudFolderDirectory,
                CloudFolderRetention = retention,
                CloudProvider = provider,
                CloudAccountId = account,
            });
        }

        var user = _settings.ReadUser();
        var country = Text(held, HouseholdSetting.ReferenceCountry, user.ReferenceCountry);
        if (!string.Equals(country, user.ReferenceCountry, StringComparison.Ordinal))
        {
            _settings.WriteUser(new UserSettings
            {
                Language = user.Language,
                ReferenceCountry = country,
                CheckForUpdatesOnStartup = user.CheckForUpdatesOnStartup,
                LogDatabaseQueries = user.LogDatabaseQueries,
            });
        }
    }

    private static string Text(IReadOnlyDictionary<string, string?> held, string setting, string current)
        => held.TryGetValue(setting, out var value) ? value ?? string.Empty : current;

    private static int Integer(IReadOnlyDictionary<string, string?> held, string setting, int current)
        => held.TryGetValue(setting, out var value)
           && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : current;

    private static bool Boolean(IReadOnlyDictionary<string, string?> held, string setting, bool current)
        => held.TryGetValue(setting, out var value) ? value == "true" : current;
}
