using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Sync;

namespace MedReminder.Infrastructure.Household;

// IHouseholdProfileInstaller over the profiles folder (household step H3c).
// A staging folder is profiles\.join-<guid>\: the profile registry never
// lists it, and Install moves it to profiles\<id>\ with the same files a
// single-group join leaves (medreminder.db, sync.settings.json and the
// protected group key). The key store is the host's (DPAPI on Windows).
internal sealed class HouseholdProfileInstaller : IHouseholdProfileInstaller
{
    public const string DatabaseFileName = "medreminder.db";
    private const string StagingPrefix = ".join-";

    private readonly string _profilesRoot;
    private readonly Func<string, ISyncKeyStore> _keyStoreIn;

    public HouseholdProfileInstaller(string profilesRoot, Func<string, ISyncKeyStore> keyStoreIn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRoot);
        ArgumentNullException.ThrowIfNull(keyStoreIn);
        _profilesRoot = profilesRoot;
        _keyStoreIn = keyStoreIn;
    }

    public ProfileStaging CreateStaging()
    {
        var directory = Path.Combine(_profilesRoot, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new ProfileStaging(directory, Path.Combine(directory, DatabaseFileName));
    }

    public void Install(ProfileStaging staging, string profileId, SyncSettings settings, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(key);
        // A profile id is a folder name; a household id from another device
        // must not name a path outside the profiles folder.
        if (profileId.IndexOfAny([.. Path.GetInvalidFileNameChars(), '.']) >= 0)
            throw new ArgumentException("Invalid profile id.", nameof(profileId));
        var target = Path.Combine(_profilesRoot, profileId);
        if (Directory.Exists(target)) throw new IOException($"The folder of profile {profileId} already exists.");

        new JsonSyncSettingsStore(Path.Combine(staging.Directory, JsonSyncSettingsStore.FileName)).Save(settings);
        _keyStoreIn(staging.Directory).Save(settings.GroupId, settings.KeyVersion, key);
        Directory.Move(staging.Directory, target);
    }

    public void Discard(ProfileStaging staging)
    {
        ArgumentNullException.ThrowIfNull(staging);
        try
        {
            if (Directory.Exists(staging.Directory)) Directory.Delete(staging.Directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover .join-* folder holds no profile; it is harmless.
        }
    }
}
