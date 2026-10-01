using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.Sync;

// ISyncSetupService for the desktop (B.1 Phase 3d). Join and rebuild
// build the group's database next to the profile database, then swap it
// in like an import (ProfileDatabaseSwap); the UI restarts the
// application. Nothing is logged about the passphrase or the key.
// Phase 4c: join with a pairing code, and the rekey after a key rotation
// on another device (rebuild plus carry-over of this device's own
// operations, applied to the new database before the restart).
internal sealed class SyncSetupService : ISyncSetupService
{
    private readonly ICurrentProfile _profile;
    private readonly MedReminderDbContext _db;
    private readonly CreateSyncGroup _create;
    private readonly JoinSyncGroup _join;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncTransportFactory _transports;
    private readonly ISyncOperationRepository _operations;
    private readonly ApplyRemoteOperations _apply;
    private readonly TimeProvider _clock;
    private readonly IDatabaseExclusiveAccess _exclusiveAccess;
    private readonly ILogger<SyncSetupService> _log;

    public SyncSetupService(
        ICurrentProfile profile,
        MedReminderDbContext db,
        CreateSyncGroup create,
        JoinSyncGroup join,
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        ISyncTransportFactory transports,
        ISyncOperationRepository operations,
        ApplyRemoteOperations apply,
        TimeProvider clock,
        IDatabaseExclusiveAccess exclusiveAccess,
        ILogger<SyncSetupService> log)
    {
        _profile = profile;
        _db = db;
        _create = create;
        _join = join;
        _settings = settings;
        _keys = keys;
        _transports = transports;
        _operations = operations;
        _apply = apply;
        _clock = clock;
        _exclusiveAccess = exclusiveAccess;
        _log = log;
    }

    public Task<IReadOnlyList<Guid>> ListGroupsAsync(SyncTarget target, CancellationToken cancellationToken)
        => JoinSyncGroup.ListGroupsAsync(_transports.Create(target), cancellationToken);

    public Task<IReadOnlyList<SyncGroupCandidate>> FindGroupsAsync(SyncTarget target, char[] passphrase,
        CancellationToken cancellationToken)
        => _join.FindGroupsAsync(_transports.Create(target), passphrase, cancellationToken);

    public async Task CreateAsync(SyncTarget target, char[] passphrase, string deviceName, CancellationToken cancellationToken)
    {
        var settings = await _create.ExecuteAsync(_transports.Create(target), passphrase, target,
            cancellationToken, deviceName: deviceName);
        _log.LogInformation("Sync enabled for profile {ProfileId} with group {GroupId}.", _profile.Id, settings.GroupId);
    }

    public Task JoinAsync(SyncTarget target, Guid groupId, char[] passphrase, string deviceName,
        CancellationToken cancellationToken)
        => JoinCoreAsync(target, groupId, new SyncKeySource.Passphrase(passphrase), deviceName, cancellationToken);

    public Task JoinWithPairingAsync(SyncTarget target, SyncPairingCode code, string deviceName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        return JoinCoreAsync(target, code.GroupId, new SyncKeySource.Pairing(code), deviceName, cancellationToken);
    }

    private async Task JoinCoreAsync(SyncTarget target, Guid groupId, SyncKeySource source, string deviceName,
        CancellationToken cancellationToken)
    {
        if (_settings.Load() is not null) throw new InvalidOperationException("Sync is already enabled.");
        var temp = TempPath();
        try
        {
            var result = await _join.ExecuteAsync(_transports.Create(target), groupId, source, temp,
                target, cancellationToken, deviceName);
            try
            {
                await ProfileDatabaseSwap.ReplaceAsync(
                    _exclusiveAccess, _db, _profile.DatabasePath, temp, _clock, cancellationToken);
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
        var key = _keys.Load(current.GroupId, current.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");
        var temp = TempPath();
        try
        {
            var rebuilt = await _join.RejoinAsync(_transports.Create(SyncTarget.Of(current)), current, key, temp,
                cancellationToken);
            await ProfileDatabaseSwap.ReplaceAsync(
                _exclusiveAccess, _db, _profile.DatabasePath, temp, _clock, cancellationToken);
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

    public async Task<(int Carried, int Dropped)> RekeyAsync(SyncKeySource source, CancellationToken cancellationToken)
    {
        var current = _settings.Load() ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        // Read before the swap: the new database holds only what the
        // rotating device had.
        var own = (await _operations.ListAllAsync(cancellationToken))
            .Where(o => o.DeviceId == current.DeviceId && o.Generation == current.Generation)
            .ToList();
        var temp = TempPath();
        try
        {
            var result = await _join.RekeyAsync(_transports.Create(SyncTarget.Of(current)), current, source, temp,
                cancellationToken);
            try
            {
                await ProfileDatabaseSwap.ReplaceAsync(
                    _exclusiveAccess, _db, _profile.DatabasePath, temp, _clock, cancellationToken);
                // Nothing read from the replaced file may be written back.
                _db.ChangeTracker.Clear();
                _keys.Save(result.Settings.GroupId, result.Settings.KeyVersion, result.Key);
                _settings.Save(result.Settings);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(result.Key);
            }
            var (carried, dropped) = await _apply.ApplyCarriedAsync(own, cancellationToken);
            _log.LogInformation(
                "Profile {ProfileId} took key version {KeyVersion} and generation {Generation}: " +
                "{Carried} own operations carried over, {Dropped} dropped.",
                _profile.Id, result.Settings.KeyVersion, result.Settings.Generation, carried, dropped);
            return (carried, dropped);
        }
        finally
        {
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
