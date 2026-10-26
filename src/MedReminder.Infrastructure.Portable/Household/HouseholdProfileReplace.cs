using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MedReminder.Infrastructure.Household;

// Replaces the open profile with its group in the installation (household
// step H3d): after this device joined again an installation it had left
// keeping the profile, whose copy here no longer syncs with the group
// granted to it (JoinedProfileStatus.Conflict). The group's database is
// built next to the profile database and swapped in like a rebuild
// (ProfileDatabaseSwap keeps the current file as <db>.bak-<timestamp>);
// the host restarts afterwards. Changes made on this device while it was
// out of the installation stay only in that copy.
//
// Nothing is logged about the keys.
public sealed class HouseholdProfileReplace
{
    private readonly string _databasePath;
    private readonly string _profileId;
    private readonly MedReminderDbContext _db;
    private readonly HouseholdKeyring _keyring;
    private readonly IHouseholdStore _store;
    private readonly JoinSyncGroup _join;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncTransportFactory _transports;
    private readonly IDatabaseExclusiveAccess _exclusiveAccess;
    private readonly TimeProvider _clock;

    internal HouseholdProfileReplace(string databasePath, string profileId, MedReminderDbContext db,
        HouseholdKeyring keyring, IHouseholdStore store, JoinSyncGroup join, ISyncSettingsStore settings,
        ISyncKeyStore keys, ISyncTransportFactory transports, IDatabaseExclusiveAccess exclusiveAccess,
        TimeProvider clock)
    {
        _databasePath = databasePath;
        _profileId = profileId;
        _db = db;
        _keyring = keyring;
        _store = store;
        _join = join;
        _settings = settings;
        _keys = keys;
        _transports = transports;
        _exclusiveAccess = exclusiveAccess;
        _clock = clock;
    }

    // InvalidOperationException when this device is not in a published
    // installation, holds no grant for the open profile, or the profile
    // already syncs with the granted group.
    public async Task ExecuteAsync(string deviceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        var identity = await _store.EnsureCreatedAsync(cancellationToken);
        if (identity.Storage is not { } storage)
            throw new InvalidOperationException("This device is not in a published installation.");
        var grant = await _keyring.OpenGrantAsync(_profileId, cancellationToken)
            ?? throw new InvalidOperationException("The installation granted this device no key for the profile.");
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_databasePath))!,
            $"medreminder.join-{Guid.NewGuid():N}.db");
        try
        {
            if (_settings.Load()?.GroupId == grant.GroupId)
                throw new InvalidOperationException("The profile already syncs with the granted group.");
            var joined = await _join.ExecuteAsync(_transports.Create(storage), grant.GroupId,
                new SyncKeySource.Known(grant.KeyVersion, grant.Key), temp, storage, cancellationToken, deviceName);
            try
            {
                await ProfileDatabaseSwap.ReplaceAsync(_exclusiveAccess, _db, _databasePath, temp, _clock,
                    cancellationToken);
                _db.ChangeTracker.Clear();
                // The key first: settings without it would turn sync on with
                // nothing to open the group.
                _keys.Save(joined.Settings.GroupId, joined.Settings.KeyVersion, joined.Key);
                _settings.Save(joined.Settings);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(joined.Key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(grant.Key);
            if (File.Exists(temp))
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
