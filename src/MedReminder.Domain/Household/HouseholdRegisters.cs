using System.Globalization;
using MedReminder.Domain.Sync;

namespace MedReminder.Domain.Household;

// Last-writer-wins registers of the household (step H2): what each
// operation writes, and the state they add up to. A register keeps every
// version it received; the winner is the one with the greatest HLC, so
// every device that holds the same versions reads the same state
// whatever the order they arrived in.
public static class HouseholdRegisters
{
    // The entity of the installation settings: no profile has this id.
    public const string Installation = "";

    // Step H3b: entities of the keys (never a profile id, which is
    // "default" or 32 hex digits).
    public const string Recovery = "recovery";
    public const string PublicKey = "PublicKey";
    public const string Escrow = "Escrow";

    // Step H4a: the entity of the master registers.
    public const string MasterEntity = "master";
    public const string Election = "Election";
    public const string Activation = "Activation";
    public const string Release = "Release";

    public static string DeviceEntity(Guid deviceId) => "device:" + deviceId.ToString("N");

    public static string GrantRegister(Guid deviceId) => "Grant:" + deviceId.ToString("N");

    public const string Registered = "Registered";
    public const string Name = "Name";
    public const string Role = "Role";
    public const string Pin = "Pin";
    public const string Removed = "Removed";

    public static IReadOnlyList<(string Register, string? Value)> WritesOf(HouseholdOperationBody body) => body switch
    {
        ProfileRegistered r =>
        [
            (Registered, r.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            (Name, r.DisplayName),
            (Role, r.Role),
        ],
        ProfileRenamed r => [(Name, r.DisplayName)],
        ProfileRoleChanged r => [(Role, r.Role)],
        ProfilePinChanged p => [(Pin, PinValue(p.Hash, p.Salt, p.Iterations))],
        ProfileRemoved => [(Removed, "true")],
        HouseholdSettingChanged s => [(s.Setting, s.Value)],
        DeviceKeyPublished d => [(PublicKey, d.PublicKey)],
        RecoveryKeyPublished r => [(PublicKey, Wrapped(r.KeyVersion, Guid.Empty, r.PublicKey))],
        ProfileKeyGranted g => [(GrantRegister(g.DeviceId), Wrapped(g.KeyVersion, g.GroupId, g.WrappedKey))],
        ProfileKeyRevoked r => [(GrantRegister(r.DeviceId), null)],
        ProfileKeyEscrowed e => [(Escrow, Wrapped(e.KeyVersion, e.GroupId, e.WrappedKey))],
        DeviceRemoved => [(Removed, "true")],
        MasterElected m => [(Election, $"{m.ElectionId:N}:{m.DeviceId:N}:{m.Kind}")],
        MasterActivated a => [(Activation, $"{a.ElectionId:N}:{a.DeviceId:N}")],
        MasterReleased r => [(Release, r.ElectionId.ToString("N"))],
        _ => throw new NotSupportedException($"No registers for {body.GetType().Name}."),
    };

    // "<iterations>:<salt>:<hash>", or null when the PIN is cleared.
    public static string? PinValue(string? hash, string? salt, int iterations)
        => string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt) || iterations <= 0
            ? null
            : $"{iterations.ToString(CultureInfo.InvariantCulture)}:{salt}:{hash}";

    // The parts of a PinValue; false for null or a malformed value.
    public static bool TryParsePin(string? value, out string hash, out string salt, out int iterations)
    {
        hash = salt = string.Empty;
        iterations = 0;
        var parts = value?.Split(':');
        if (parts is not { Length: 3 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations <= 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            iterations = 0;
            return false;
        }
        salt = parts[1];
        hash = parts[2];
        return true;
    }

    // "<groupId>:<keyVersion>:<wrapped>" (no colon in a wrapped key).
    private static string Wrapped(int keyVersion, Guid groupId, string wrapped)
        => $"{groupId:N}:{keyVersion.ToString(CultureInfo.InvariantCulture)}:{wrapped}";

    private static HouseholdWrappedKey? ParseWrapped(string? value)
    {
        var parts = value?.Split(':');
        return parts is { Length: 3 } && Guid.TryParseExact(parts[0], "N", out var group)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? new HouseholdWrappedKey(group, version, parts[2])
            : null;
    }

    // Step H3b: the keys the winning versions describe.
    public static HouseholdKeys Keys(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
    {
        var winners = versions
            .GroupBy(v => (v.ProfileId, v.Register))
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value);
        var devices = new Dictionary<Guid, string>();
        var removed = new HashSet<Guid>();
        HouseholdWrappedKey? recovery = null;
        var escrows = new Dictionary<string, HouseholdWrappedKey>(StringComparer.Ordinal);
        var grants = new Dictionary<(string ProfileId, Guid DeviceId), HouseholdWrappedKey>();
        foreach (var ((entity, register), value) in winners)
        {
            if (value is null) continue;
            if (entity.StartsWith("device:", StringComparison.Ordinal) && register == PublicKey
                && Guid.TryParseExact(entity["device:".Length..], "N", out var device))
            {
                devices[device] = value;
            }
            else if (entity.StartsWith("device:", StringComparison.Ordinal) && register == Removed
                     && Guid.TryParseExact(entity["device:".Length..], "N", out var gone))
            {
                removed.Add(gone);
            }
            else if (entity == Recovery && register == PublicKey)
            {
                recovery = ParseWrapped(value);
            }
            else if (register == Escrow && ParseWrapped(value) is { } escrow)
            {
                escrows[entity] = escrow;
            }
            else if (register.StartsWith("Grant:", StringComparison.Ordinal)
                     && Guid.TryParseExact(register["Grant:".Length..], "N", out var grantee)
                     && ParseWrapped(value) is { } grant)
            {
                grants[(entity, grantee)] = grant;
            }
        }
        // Step H5a: a removed device has no public key to grant to.
        foreach (var gone in removed) devices.Remove(gone);
        return new HouseholdKeys(devices, recovery, escrows, grants, removed);
    }

    // Step H4a: the master registers.
    public static HouseholdMaster Master(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
    {
        var winners = versions
            .Where(v => v.ProfileId == MasterEntity)
            .GroupBy(v => v.Register, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value, StringComparer.Ordinal);
        MasterElection? election = null;
        if (winners.GetValueOrDefault(Election)?.Split(':') is [var e, var d, var kind]
            && Guid.TryParseExact(e, "N", out var electionId) && Guid.TryParseExact(d, "N", out var device))
        {
            election = new MasterElection(electionId, device, kind);
        }
        MasterActivation? activation = null;
        if (winners.GetValueOrDefault(Activation)?.Split(':') is [var ae, var ad]
            && Guid.TryParseExact(ae, "N", out var activated) && Guid.TryParseExact(ad, "N", out var activeDevice))
        {
            activation = new MasterActivation(activated, activeDevice);
        }
        Guid? released = Guid.TryParseExact(winners.GetValueOrDefault(Release), "N", out var r) ? r : null;
        return new HouseholdMaster(election, activation, released);
    }

    // The winning value of each installation setting that has a version.
    public static IReadOnlyDictionary<string, string?> Settings(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
        => versions
            .Where(v => v.ProfileId == Installation)
            .GroupBy(v => v.Register, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value, StringComparer.Ordinal);

    // The profiles the winning versions describe. A profile exists once it
    // is registered and until it is removed; a removed profile never comes
    // back.
    public static IReadOnlyList<HouseholdProfile> Profiles(
        IEnumerable<(string ProfileId, string Register, HybridTimestamp Version, string? Value)> versions)
    {
        var winners = versions
            .GroupBy(v => (v.ProfileId, v.Register))
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version).Value);
        return [.. winners.Keys.Select(k => k.ProfileId).Distinct(StringComparer.Ordinal)
            .Where(id => winners.ContainsKey((id, Registered)) && !winners.ContainsKey((id, Removed)))
            .Order(StringComparer.Ordinal)
            .Select(id => new HouseholdProfile(
                id,
                winners.GetValueOrDefault((id, Name)) ?? string.Empty,
                winners.GetValueOrDefault((id, Role)) ?? HouseholdRole.User,
                winners.GetValueOrDefault((id, Pin))))];
    }
}

// A profile as the household holds it. Pin is the register value of
// HouseholdRegisters.PinValue, null without a PIN.
public sealed record HouseholdProfile(string ProfileId, string DisplayName, string Role, string? Pin);

// Step H4a. Election: the current one (last by HLC). Activation: the last
// one recorded, possibly of an older election. Released: the last election
// an outgoing master released.
public sealed record HouseholdMaster(MasterElection? Election, MasterActivation? Activation, Guid? Released)
{
    public static readonly HouseholdMaster None = new(null, null, null);

    // The active master: the device of the current election once activated.
    public Guid? ActiveDevice => Election is { } e && Activation?.ElectionId == e.ElectionId ? e.DeviceId : null;

    // The device still active under an older election, while the current
    // one waits for activation.
    public Guid? OutgoingDevice
        => Election is { } e && Activation is { } a && a.ElectionId != e.ElectionId ? a.DeviceId : null;

    public bool Pending => Election is { } e && Activation?.ElectionId != e.ElectionId;
}

public sealed record MasterElection(Guid ElectionId, Guid DeviceId, string Kind);

public sealed record MasterActivation(Guid ElectionId, Guid DeviceId);

// Step H3b. For RecoveryKeyPublished, GroupId is empty and Wrapped is the
// recovery public key.
public sealed record HouseholdWrappedKey(Guid GroupId, int KeyVersion, string Wrapped);

public sealed record HouseholdKeys(
    IReadOnlyDictionary<Guid, string> DevicePublicKeys,
    HouseholdWrappedKey? RecoveryPublicKey,
    IReadOnlyDictionary<string, HouseholdWrappedKey> Escrows,
    IReadOnlyDictionary<(string ProfileId, Guid DeviceId), HouseholdWrappedKey> Grants,
    // Step H5a.
    IReadOnlySet<Guid>? RemovedDevices = null)
{
    public bool IsRemoved(Guid deviceId) => RemovedDevices?.Contains(deviceId) == true;
}
