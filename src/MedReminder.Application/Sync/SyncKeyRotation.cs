using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// Changes the group key and the sync passphrase (B.1 Phase 4c,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §6.2): to remove a lost
// device, or after the passphrase became known to someone else.
//
// The files of the current generation are sealed with the old key, which
// the removed device holds, and a new key cannot reach the other devices
// through a file the old key opens. So the rotation starts a new
// generation, like an import (§5.7): key.<v+1>.wrap with the new
// passphrase, then a genesis of this device's database sealed with the
// new key. The other devices find a generation they cannot open and stop
// publishing; each one enters the new passphrase or a pairing code,
// rebuilds from the new genesis and carries over its own operations
// (JoinSyncGroup.RekeyAsync, ApplyRemoteOperations.ApplyCarriedAsync). A
// device that never gets the new key reads nothing written after the
// rotation; what it held before stays readable to it (inherent).
//
// The caller runs the engine first, so the genesis holds this device's
// latest operations and the latest segments of the other devices.
public sealed class RotateSyncKey
{
    private readonly SyncGenesis _genesis;
    private readonly ISyncSnapshotStore _snapshots;
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncPeerRepository _peers;
    private readonly IArchiveCipher _cipher;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly SyncEngineOptions _options;

    public RotateSyncKey(
        SyncGenesis genesis,
        ISyncSnapshotStore snapshots,
        ISyncSettingsStore settings,
        ISyncKeyStore keys,
        ISyncPeerRepository peers,
        IArchiveCipher cipher,
        IUnitOfWork uow,
        TimeProvider clock,
        SyncEngineOptions? options = null)
    {
        _genesis = genesis;
        _snapshots = snapshots;
        _settings = settings;
        _keys = keys;
        _peers = peers;
        _cipher = cipher;
        _uow = uow;
        _clock = clock;
        _options = options ?? new SyncEngineOptions();
    }

    public async Task<SyncSettings> ExecuteAsync(ISyncTransport transport, char[] passphrase,
        CancellationToken cancellationToken, Argon2Params? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(passphrase);
        var current = _settings.Load() ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        if (current.ResetPending) throw new InvalidOperationException("A new sync generation must be started first.");
        var held = _keys.Load(current.GroupId, current.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");
        CryptographicOperations.ZeroMemory(held);

        // A device that is behind would put an outdated database in the
        // genesis and discard what the others recorded since.
        if ((await SyncKeys.KeyVersionsAsync(transport, current.GroupId, cancellationToken)).FirstOrDefault() > current.KeyVersion)
            throw new InvalidOperationException("The group key was changed on another device; this device needs the new key first.");
        if (await SyncKeys.CurrentGenerationAsync(transport, current.GroupId, current.KeyVersion, cancellationToken) != current.Generation)
            throw new InvalidOperationException("A newer sync generation exists; rebuild this device first.");

        await _genesis.RecordAsync(cancellationToken);
        return await WriteGate.RunExclusiveAsync(async ct =>
        {
            var version = (await SyncKeys.KeyVersionsAsync(transport, current.GroupId, ct)).FirstOrDefault() + 1;
            var key = RandomNumberGenerator.GetBytes(SyncKeyWrap.KeySize);
            try
            {
                // Wrap first: an interruption leaves a wrap without a
                // generation, which the other devices ignore.
                if (!await transport.CreateAsync(SyncLayout.KeyWrap(current.GroupId, version),
                        SyncKeyWrap.Wrap(_cipher, current.GroupId, version, key, passphrase, kdf ?? Argon2Params.Default).ToBytes(), ct))
                {
                    throw new InvalidOperationException("The group key is being changed on another device.");
                }
                var generation = await SyncEngine.LatestGenerationAsync(transport, current.GroupId, ct) + 1;
                var settings = current with { Generation = generation, KeyVersion = version, ResetPending = false };
                await CreateSyncGroup.WriteGenesisAsync(transport, _cipher, _snapshots, settings, key, ct);
                await SyncEngine.WriteRecordAsync(transport, _cipher, key, settings, _options, _clock, 0,
                    new Dictionary<Guid, int>(), ct);

                await _peers.ClearAsync(ct);
                await _peers.AddAsync(new SyncPeer { DeviceId = settings.DeviceId, Generation = generation }, ct);
                await _uow.SaveChangesAsync(ct);
                _keys.Save(settings.GroupId, version, key);
                _settings.Save(settings);
                return settings;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }, cancellationToken);
    }
}

public sealed record SyncPairingOffer(SyncPairingCode Code, DateTimeOffset ExpiresAt);

// Pairing offers (B.1 Phase 4c, §6.1): a device of the group writes the
// current group key to its pairing file, encrypted with a random secret
// that only the QR code carries, valid for 10 minutes. The offer ends
// when the dialog closes (EndAsync deletes the file) or when it expires
// (the joining device refuses it). A new offer replaces the previous one.
public sealed class SyncPairingOffers
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly IArchiveCipher _cipher;
    private readonly TimeProvider _clock;

    public SyncPairingOffers(ISyncSettingsStore settings, ISyncKeyStore keys, IArchiveCipher cipher, TimeProvider clock)
    {
        _settings = settings;
        _keys = keys;
        _cipher = cipher;
        _clock = clock;
    }

    public async Task<SyncPairingOffer> StartAsync(ISyncTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var settings = _settings.Load() ?? throw new InvalidOperationException("Sync is not enabled for this profile.");
        if (settings.ResetPending) throw new InvalidOperationException("A new sync generation must be started first.");
        // A device waiting for a new key would hand out the old one.
        if ((await SyncKeys.KeyVersionsAsync(transport, settings.GroupId, cancellationToken)).FirstOrDefault() > settings.KeyVersion)
            throw new InvalidOperationException("The group key was changed on another device; this device needs the new key first.");

        var key = _keys.Load(settings.GroupId, settings.KeyVersion)
            ?? throw new InvalidOperationException("The group key is not stored on this device.");
        try
        {
            var code = new SyncPairingCode(settings.GroupId, settings.DeviceId, settings.Provider,
                RandomNumberGenerator.GetBytes(SyncPairingCode.SecretSize));
            var expiresAt = _clock.GetUtcNow() + Lifetime;
            await transport.WriteAsync(SyncLayout.Pairing(settings.GroupId, settings.DeviceId),
                SyncPairingFile.Seal(_cipher, code, settings.KeyVersion, key, expiresAt).ToBytes(), cancellationToken);
            return new SyncPairingOffer(code, expiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task EndAsync(ISyncTransport transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (_settings.Load() is not { } settings) return;
        await transport.DeleteAsync(SyncLayout.Pairing(settings.GroupId, settings.DeviceId), cancellationToken);
    }
}
