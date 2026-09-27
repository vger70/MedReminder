using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Sync;

// ISyncSetupService for the desktop (B.1 Phase 3d). Join and rebuild
// build the group's database next to the profile database, then swap it
// in like an import (ProfileDatabaseSwap); the UI restarts the
// application. Nothing is logged about the passphrase or the key.
internal sealed class SyncSetupService : ISyncSetupService
{
    private readonly ICurrentProfile _profile;
    private readonly MedReminderDbContext _db;
    private readonly CreateSyncGroup _create;
    private readonly JoinSyncGroup _join;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly TimeProvider _clock;
    private readonly ILogger<SyncSetupService> _log;

    public SyncSetupService(
        ICurrentProfile profile,
        MedReminderDbContext db,
        CreateSyncGroup create,
        JoinSyncGroup join,
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        TimeProvider clock,
        ILogger<SyncSetupService> log)
    {
        _profile = profile;
        _db = db;
        _create = create;
        _join = join;
        _settings = settings;
        _keys = keys;
        _clock = clock;
        _log = log;
    }

    public Task<IReadOnlyList<Guid>> ListGroupsAsync(string folder, CancellationToken cancellationToken)
        => JoinSyncGroup.ListGroupsAsync(new LocalFolderSyncTransport(folder), cancellationToken);

    public async Task CreateAsync(string folder, char[] passphrase, string deviceName, CancellationToken cancellationToken)
    {
        var settings = await _create.ExecuteAsync(new LocalFolderSyncTransport(folder), passphrase, folder,
            cancellationToken, deviceName: deviceName);
        _log.LogInformation("Sync enabled for profile {ProfileId} with group {GroupId}.", _profile.Id, settings.GroupId);
    }

    public async Task JoinAsync(string folder, Guid groupId, char[] passphrase, string deviceName,
        CancellationToken cancellationToken)
    {
        if (_settings.Load() is not null) throw new InvalidOperationException("Sync is already enabled.");
        var temp = TempPath();
        try
        {
            var result = await _join.ExecuteAsync(new LocalFolderSyncTransport(folder), groupId, passphrase, temp,
                folder, cancellationToken, deviceName);
            try
            {
                ProfileDatabaseSwap.Replace(_db, _profile.DatabasePath, temp, _clock);
                _keys.Save(result.Settings.GroupId, result.Settings.KeyVersion, result.Key);
                _settings.Save(result.Settings);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(result.Key);
            }
            _log.LogInformation("Profile {ProfileId} joined sync group {GroupId}.", _profile.Id, groupId);
        }
        finally
        {
            DeleteIfPresent(temp);
        }
    }

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        var current = _settings.Load() ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        var folder = current.Folder ?? throw new InvalidOperationException("No sync folder is configured.");
        var key = _keys.Load(current.GroupId, current.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");
        var temp = TempPath();
        try
        {
            var rebuilt = await _join.RejoinAsync(new LocalFolderSyncTransport(folder), current, key, temp,
                cancellationToken);
            ProfileDatabaseSwap.Replace(_db, _profile.DatabasePath, temp, _clock);
            _settings.Save(rebuilt with { ResetPending = false });
            _log.LogInformation("Profile {ProfileId} rebuilt from sync generation {Generation}.",
                _profile.Id, rebuilt.Generation);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeleteIfPresent(temp);
        }
    }

    private string TempPath()
        => Path.Combine(_profile.DataDirectory, $"medreminder.sync-{Guid.NewGuid():N}.db");

    private static void DeleteIfPresent(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
