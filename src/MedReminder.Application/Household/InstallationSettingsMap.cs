using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Household;

namespace MedReminder.Application.Household;

// The household value of each installation setting (step H2b), from the
// settings objects the app already uses. Only what belongs to the
// installation: the backup folders, the local backup policy, the language
// and the update check stay on the device (ANALYSIS-HOUSEHOLD-MASTER-
// DEVICE.md §4.3). The SMTP password is handled apart (a secret).
public static class InstallationSettingsMap
{
    public static IReadOnlyDictionary<string, string?> Of(SmtpTransport smtp) => new Dictionary<string, string?>
    {
        [HouseholdSetting.SmtpHost] = smtp.Host,
        [HouseholdSetting.SmtpPort] = Integer(smtp.Port),
        [HouseholdSetting.SmtpUseStartTls] = Boolean(smtp.UseStartTls),
        [HouseholdSetting.SmtpUsername] = smtp.Username,
        [HouseholdSetting.SmtpFromAddress] = smtp.FromAddress,
        [HouseholdSetting.SmtpFromDisplayName] = smtp.FromDisplayName,
        [HouseholdSetting.SmtpTimeoutSeconds] = Integer(smtp.TimeoutSeconds),
    };

    // The scheduled cloud backup policy: whether it runs, how many
    // snapshots it keeps, and the provider account. The folder of a
    // folder target is a path of this device.
    public static IReadOnlyDictionary<string, string?> Of(BackupSettings backup) => new Dictionary<string, string?>
    {
        [HouseholdSetting.CloudBackupEnabled] = Boolean(backup.CloudFolderEnabled),
        [HouseholdSetting.CloudBackupRetention] = Integer(backup.CloudFolderRetention),
        [HouseholdSetting.CloudBackupProvider] = backup.CloudProvider?.ToString(),
        [HouseholdSetting.CloudBackupAccountId] = backup.CloudAccountId,
    };

    public static IReadOnlyDictionary<string, string?> Of(UserSettings user) => new Dictionary<string, string?>
    {
        [HouseholdSetting.ReferenceCountry] = user.ReferenceCountry,
    };

    // The settings whose value differs from what the household holds.
    public static IReadOnlyList<HouseholdSettingChanged> Changes(
        IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, string?> held)
        => [.. values
            .Where(v => !held.TryGetValue(v.Key, out var known) || !string.Equals(known, v.Value, StringComparison.Ordinal))
            .OrderBy(v => v.Key, StringComparer.Ordinal)
            .Select(v => new HouseholdSettingChanged(v.Key, v.Value))];

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Boolean(bool value) => value ? "true" : "false";
}
