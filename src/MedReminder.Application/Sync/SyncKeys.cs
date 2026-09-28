using System.Security.Cryptography;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;

namespace MedReminder.Application.Sync;

// How a device obtains the group key (B.1 Phase 4c, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §6.1): the sync passphrase, or a pairing
// code shown by a device of the group.
public abstract record SyncKeySource
{
    private SyncKeySource()
    {
    }

    // The caller zeroes the array after use.
    public sealed record Passphrase(char[] Value) : SyncKeySource;

    public sealed record Pairing(SyncPairingCode Code) : SyncKeySource;
}

// A group key, its version and the generation sealed with it. The caller
// zeroes Key after use.
public sealed record SyncGroupKey(int KeyVersion, int Generation, byte[] Key);

// Key versions and generations in the remote storage (Phase 4c,
// docs/SYNC-FORMAT.md §2, §7). A generation has one key version: the
// one its genesis is sealed with, read from the cleartext header. A key
// rotation starts a new generation sealed with the new key, so a device
// only ever needs the key of its current generation.
public static class SyncKeys
{
    // Versions of the key wraps present, highest first.
    public static async Task<IReadOnlyList<int>> KeyVersionsAsync(ISyncTransport transport, Guid groupId,
        CancellationToken cancellationToken)
        => [.. (await transport.ListAsync(SyncLayout.Group(groupId) + "/", cancellationToken))
            .Select(p => SyncLayout.TryParseNumber(p, "key.", ".wrap", out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .OrderDescending()];

    // Key version a generation's genesis is sealed with; null when the
    // genesis is missing or its header cannot be read. The header is not
    // authenticated here: the genesis is opened with that key later.
    public static async Task<int?> GenesisKeyVersionAsync(ISyncTransport transport, Guid groupId, int generation,
        CancellationToken cancellationToken)
    {
        var file = await transport.ReadAsync(SyncLayout.Genesis(groupId, generation), cancellationToken);
        if (file is null) return null;
        try
        {
            var header = SyncFileCodec.ReadHeader(file);
            return header.Kind == SyncFileKind.Genesis && header.GroupId == groupId && header.Generation == generation
                ? header.KeyVersion
                : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return null;
        }
    }

    // The highest generation sealed with a key version; 0 when none is.
    // A generation sealed with another key is skipped: an older key may
    // be held by a removed device (§6.2). The cache avoids downloading a
    // genesis twice when several key versions are tried.
    public static async Task<int> CurrentGenerationAsync(ISyncTransport transport, Guid groupId, int keyVersion,
        CancellationToken cancellationToken, Dictionary<int, int?>? cache = null)
    {
        cache ??= new Dictionary<int, int?>();
        var generations = (await transport.ListAsync(SyncLayout.GenesisFolder(groupId), cancellationToken))
            .Select(p => SyncLayout.TryParseNumber(p, string.Empty, ".mrg", out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .OrderDescending();
        foreach (var generation in generations)
        {
            if (!cache.TryGetValue(generation, out var sealedWith))
            {
                sealedWith = await GenesisKeyVersionAsync(transport, groupId, generation, cancellationToken);
                cache[generation] = sealedWith;
            }
            if (sealedWith == keyVersion) return generation;
        }
        return 0;
    }

    // The group key from a passphrase or a pairing code, with the
    // generation that uses it.
    //
    // Passphrase: the newest key version that has a generation (a wrap
    // without one is left by an interrupted rotation). Only that wrap is
    // tried: an older passphrase opens an older key, whose generation is
    // over. Throws CryptographicException for a wrong passphrase.
    //
    // Pairing code: the pairing file of the showing device. Throws
    // SyncPairingExpiredException when the offer is over, and
    // CryptographicException when a newer offer replaced it.
    public static async Task<SyncGroupKey> ObtainAsync(ISyncTransport transport, IArchiveCipher cipher, Guid groupId,
        SyncKeySource source, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(source);
        var cache = new Dictionary<int, int?>();
        switch (source)
        {
            case SyncKeySource.Passphrase passphrase:
                foreach (var version in await KeyVersionsAsync(transport, groupId, cancellationToken))
                {
                    var generation = await CurrentGenerationAsync(transport, groupId, version, cancellationToken, cache);
                    if (generation == 0) continue;
                    var wrap = SyncKeyWrap.Parse(await transport.ReadAsync(SyncLayout.KeyWrap(groupId, version), cancellationToken)
                        ?? throw new InvalidOperationException("The group key is missing."));
                    if (wrap.GroupId != groupId || wrap.KeyVersion != version)
                        throw new InvalidDataException("The key wrap does not match its path.");
                    return new SyncGroupKey(version, generation, wrap.Unwrap(cipher, passphrase.Value));
                }
                throw new InvalidOperationException("The sync group has no key.");

            case SyncKeySource.Pairing pairing:
            {
                if (pairing.Code.GroupId != groupId)
                    throw new ArgumentException("The pairing code belongs to another sync group.", nameof(source));
                var file = await transport.ReadAsync(SyncLayout.Pairing(groupId, pairing.Code.DeviceId), cancellationToken)
                    ?? throw new SyncPairingExpiredException();
                var (version, key) = SyncPairingFile.Parse(file).Open(cipher, pairing.Code, clock.GetUtcNow());
                var generation = await CurrentGenerationAsync(transport, groupId, version, cancellationToken, cache);
                if (generation == 0)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new InvalidOperationException("No generation of the sync group uses the key of this pairing code.");
                }
                return new SyncGroupKey(version, generation, key);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }
}
