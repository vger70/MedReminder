using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Profiles;

// IProfileSettingsStore of the desktop (B.1, P8): the display name in
// profiles.json (IProfileRegistry) and the notification recipients in
// the profile's notifications.settings.json, the file the configuration
// reloads (IOptionsMonitor<NotificationSettings>). Same file shape as
// before: { "Notifications": { "ToAddress", "CaregiverAddress",
// "DoctorAddress" } }. Addresses are not logged.
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
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!TryGetProperty(document.RootElement, Section, out var section)) return result;
        result.ToAddress = StringOf(section, nameof(NotificationSettings.ToAddress));
        result.CaregiverAddress = StringOf(section, nameof(NotificationSettings.CaregiverAddress));
        result.DoctorAddress = StringOf(section, nameof(NotificationSettings.DoctorAddress));
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

// ISyncProfileStatus of the desktop: a profile takes part in sync when
// its sync.settings.json exists (JsonSyncSettingsStore.FileName in
// Infrastructure.Portable).
internal sealed class SyncProfileStatus : ISyncProfileStatus
{
    private const string SyncSettingsFileName = "sync.settings.json";

    public bool IsSyncEnabled(string profileId)
        => File.Exists(Path.Combine(AppDataPaths.GetProfileDataDirectory(profileId), SyncSettingsFileName));
}
