using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Sync;

namespace MedReminder.Infrastructure.Profiles;

// IProfileSettingsStore (B.1, P8; portable since Android plan M0,
// backlog B0-01): the display name in
// profiles.json (IProfileRegistry) and the notification recipients in
// the profile's notifications.settings.json, the file the configuration
// reloads (IOptionsMonitor<NotificationSettings>). Same file shape as
// before: { "Notifications": { "ToAddress", "CaregiverAddress",
// "DoctorAddress", "CaregiverEmails", "CaregiverDigest",
// "CaregiverDigestSentOn", "PackageExpiryLeadDays",
// "PackageInUseLeadDays", "Region" } }. Addresses are not logged.
internal sealed class ProfileSettingsStore : IProfileSettingsStore
{
    private const string Section = NotificationSettings.SectionName;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ICurrentProfile _profile;
    private readonly IProfileRegistry _registry;
    private readonly object _lock = new();

    public ProfileSettingsStore(ICurrentProfile profile, IProfileRegistry registry)
    {
        _profile = profile;
        _registry = registry;
    }

    public IReadOnlyDictionary<string, string?> Read()
    {
        lock (_lock)
        {
            var notifications = ReadNotifications();
            return new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [ProfileSetting.DisplayName] = _registry.GetById(_profile.Id)?.DisplayName ?? _profile.DisplayName,
                [ProfileSetting.ToAddress] = notifications.ToAddress,
                [ProfileSetting.CaregiverAddress] = notifications.CaregiverAddress,
                [ProfileSetting.DoctorAddress] = notifications.DoctorAddress,
                [ProfileSetting.CaregiverEmails] = notifications.CaregiverEmails,
                [ProfileSetting.CaregiverDigest] = notifications.CaregiverDigest,
                [ProfileSetting.CaregiverDigestSentOn] = notifications.CaregiverDigestSentOn,
                [ProfileSetting.PackageExpiryLeadDays] = notifications.PackageExpiryLeadDays,
                [ProfileSetting.PackageInUseLeadDays] = notifications.PackageInUseLeadDays,
                [ProfileSetting.Region] = notifications.Region,
            };
        }
    }

    public void Write(IReadOnlyDictionary<string, string?> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        lock (_lock)
        {
            if (changes.TryGetValue(ProfileSetting.DisplayName, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                _registry.Rename(_profile.Id, name);
            }

            var addresses = changes.Keys.Where(k => k != ProfileSetting.DisplayName).ToList();
            if (addresses.Count == 0) return;
            var notifications = ReadNotifications();
            foreach (var key in addresses)
            {
                var value = changes[key] ?? string.Empty;
                switch (key)
                {
                    case ProfileSetting.ToAddress: notifications.ToAddress = value; break;
                    case ProfileSetting.CaregiverAddress: notifications.CaregiverAddress = value; break;
                    case ProfileSetting.DoctorAddress: notifications.DoctorAddress = value; break;
                    case ProfileSetting.CaregiverEmails: notifications.CaregiverEmails = value; break;
                    case ProfileSetting.CaregiverDigest: notifications.CaregiverDigest = value; break;
                    case ProfileSetting.CaregiverDigestSentOn: notifications.CaregiverDigestSentOn = value; break;
                    case ProfileSetting.PackageExpiryLeadDays: notifications.PackageExpiryLeadDays = value; break;
                    case ProfileSetting.PackageInUseLeadDays: notifications.PackageInUseLeadDays = value; break;
                    case ProfileSetting.Region: notifications.Region = value; break;
                    default: throw new ArgumentException($"Unknown profile setting '{key}'.", nameof(changes));
                }
            }
            var path = _profile.NotificationSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new Dictionary<string, NotificationSettings> { [Section] = notifications }, WriteOptions));
        }
    }

    private NotificationSettings ReadNotifications()
    {
        var path = _profile.NotificationSettingsPath;
        var result = new NotificationSettings();
        if (!File.Exists(path)) return result;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file reads as empty settings, like the
            // configuration that skips it; the next save rewrites it.
            return result;
        }
        using var _ = document;
        if (!TryGetProperty(document.RootElement, Section, out var section)) return result;
        result.ToAddress = StringOf(section, nameof(NotificationSettings.ToAddress));
        result.CaregiverAddress = StringOf(section, nameof(NotificationSettings.CaregiverAddress));
        result.DoctorAddress = StringOf(section, nameof(NotificationSettings.DoctorAddress));
        result.CaregiverEmails = StringOf(section, nameof(NotificationSettings.CaregiverEmails));
        result.CaregiverDigest = StringOf(section, nameof(NotificationSettings.CaregiverDigest));
        result.CaregiverDigestSentOn = StringOf(section, nameof(NotificationSettings.CaregiverDigestSentOn));
        result.PackageExpiryLeadDays = StringOf(section, nameof(NotificationSettings.PackageExpiryLeadDays));
        result.PackageInUseLeadDays = StringOf(section, nameof(NotificationSettings.PackageInUseLeadDays));
        result.Region = StringOf(section, nameof(NotificationSettings.Region));
        return result;
    }

    // Case-insensitive, like the configuration binder that reads the file.
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        return false;
    }

    private static string StringOf(JsonElement section, string name)
        => TryGetProperty(section, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}

// ISyncProfileStatus: a profile takes part in sync when its
// sync.settings.json exists. The host passes the root that holds one
// folder per profile (AppDataPaths.GetProfilesRootDirectory() on
// Windows).
internal sealed class SyncProfileStatus : ISyncProfileStatus
{
    private readonly string _profilesRootDirectory;

    public SyncProfileStatus(string profilesRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);
        _profilesRootDirectory = profilesRootDirectory;
    }

    public bool IsSyncEnabled(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return File.Exists(Path.Combine(_profilesRootDirectory, profileId, JsonSyncSettingsStore.FileName));
    }
}
