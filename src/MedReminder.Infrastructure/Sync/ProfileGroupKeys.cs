using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Credentials;
using MedReminder.Infrastructure.Storage;

namespace MedReminder.Infrastructure.Sync;

// IProfileGroupKeys over profiles\<id>\sync.settings.json and
// sync.protected (household step H3b), the files the sync of the open
// profile uses, read here for any profile of the installation.
internal sealed class ProfileGroupKeys : IProfileGroupKeys
{
    public ProfileGroupKey? Load(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var directory = AppDataPaths.GetProfileDataDirectory(profileId);
        var settings = new JsonSyncSettingsStore(Path.Combine(directory, JsonSyncSettingsStore.FileName)).Load();
        if (settings is null) return null;
        var key = new DpapiSyncKeyStore(Path.Combine(directory, DpapiSyncKeyStore.FileName))
            .Load(settings.GroupId, settings.KeyVersion);
        return key is null ? null : new ProfileGroupKey(settings.GroupId, settings.KeyVersion, key);
    }
}
