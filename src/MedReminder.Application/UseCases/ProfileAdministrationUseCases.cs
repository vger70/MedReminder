using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.UseCases;

// Profile administration through the household (household feature, step
// H2; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.3, §8). Each
// use case checks who may act, writes profiles.json through the registry
// as before, then records the change as a household operation so that a
// later step can replicate it. The admin checks live here, not only in
// the forms, as ExportService does.
//
// None of them takes WriteGate: profiles.json has its own lock in the
// registry and the household store its own gate; no profile database is
// touched.

public enum ProfileAdministrationError
{
    // The open profile is not an administrator.
    NotAdmin,

    // The role of the open profile cannot change (ANALYSIS-MULTI-USER-
    // ROLES-OVERVIEW.md D1), nor can the open profile be deleted.
    ActiveProfile,

    // At least one administrator must remain.
    LastAdmin,

    NotFound,
}

public sealed class ProfileAdministrationException(ProfileAdministrationError error)
    : InvalidOperationException($"Profile administration refused: {error}.")
{
    public ProfileAdministrationError Error { get; } = error;
}

internal static class ProfileAdministration
{
    public static void RequireAdmin(ICurrentProfile current)
    {
        if (!current.IsAdmin) throw new ProfileAdministrationException(ProfileAdministrationError.NotAdmin);
    }

    public static Profile RequireProfile(IProfileRegistry registry, string profileId)
        => registry.GetById(profileId) ?? throw new ProfileAdministrationException(ProfileAdministrationError.NotFound);

    public static string Wire(ProfileRole role) => role == ProfileRole.Admin ? HouseholdRole.Admin : HouseholdRole.User;

    public static ProfilePinChanged Pin(string profileId, ProfilePinHash? pin)
        => new(profileId, pin?.Hash, pin?.Salt, pin?.Iterations ?? 0);
}

// Manage profiles → New. The first profile is created by the first-run
// wizard before the host exists; ReconcileHousehold records it.
public sealed class CreateProfile
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public CreateProfile(IProfileRegistry registry, ICurrentProfile current, HouseholdLog household)
    {
        _registry = registry;
        _current = current;
        _household = household;
    }

    public async Task<Profile> ExecuteAsync(string displayName, ProfileRole role, string? pin,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ProfileAdministration.RequireAdmin(_current);
        var created = _registry.Create(displayName, role);
        if (!string.IsNullOrEmpty(pin)) _registry.SetPin(created.Id, pin);

        List<HouseholdOperationBody> operations =
            [new ProfileRegistered(created.Id, created.DisplayName, ProfileAdministration.Wire(created.Role), created.CreatedAt)];
        if (_registry.GetPinHash(created.Id) is { } hash) operations.Add(ProfileAdministration.Pin(created.Id, hash));
        await _household.AppendAsync(operations, cancellationToken);
        return _registry.GetById(created.Id) ?? created;
    }
}

// Manage profiles → Delete. Not the open profile (§14a H), never the
// last admin.
public sealed class DeleteProfile
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public DeleteProfile(IProfileRegistry registry, ICurrentProfile current, HouseholdLog household)
    {
        _registry = registry;
        _current = current;
        _household = household;
    }

    public async Task ExecuteAsync(string profileId, bool deleteData, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ProfileAdministration.RequireAdmin(_current);
        if (profileId == _current.Id) throw new ProfileAdministrationException(ProfileAdministrationError.ActiveProfile);
        var profile = ProfileAdministration.RequireProfile(_registry, profileId);
        if (profile.Role == ProfileRole.Admin
            && _registry.ListProfiles().Count(p => p.Role == ProfileRole.Admin) <= 1)
        {
            throw new ProfileAdministrationException(ProfileAdministrationError.LastAdmin);
        }
        _registry.Delete(profileId, deleteData);
        await _household.AppendAsync([new ProfileRemoved(profileId)], cancellationToken);
    }
}

// Manage profiles → Change role (item G of ANALYSIS-MULTI-USER-ROLES-
// OVERVIEW.md, absorbed by H2): an admin changes the role of another
// profile. The open profile's role is read at boot and drives the menus,
// so it cannot change while open (D1); promoting a profile without a PIN
// is allowed, the form warns (D2).
public sealed class ChangeProfileRole
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;
    private readonly ILogger<ChangeProfileRole> _log;

    public ChangeProfileRole(IProfileRegistry registry, ICurrentProfile current, HouseholdLog household,
        ILogger<ChangeProfileRole> log)
    {
        _registry = registry;
        _current = current;
        _household = household;
        _log = log;
    }

    // False when the profile already has that role (nothing written).
    public async Task<bool> ExecuteAsync(string profileId, ProfileRole role, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ProfileAdministration.RequireAdmin(_current);
        if (profileId == _current.Id) throw new ProfileAdministrationException(ProfileAdministrationError.ActiveProfile);
        var profile = ProfileAdministration.RequireProfile(_registry, profileId);
        if (profile.Role == role) return false;
        if (role != ProfileRole.Admin && _registry.ListProfiles().Count(p => p.Role == ProfileRole.Admin) <= 1)
        {
            throw new ProfileAdministrationException(ProfileAdministrationError.LastAdmin);
        }

        _registry.SetRole(profileId, role);
        await _household.AppendAsync([new ProfileRoleChanged(profileId, ProfileAdministration.Wire(role))],
            cancellationToken);
        // The id only: the display name is a person's name.
        _log.LogInformation("Role of profile {ProfileId} changed from {OldRole} to {NewRole}.",
            profileId, profile.Role, role);
        return true;
    }
}

// Settings → Change my PIN, and Manage profiles → Change PIN: a profile
// changes its own PIN, an admin any PIN. A null pin clears it.
public sealed class SetProfilePin
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly HouseholdLog _household;

    public SetProfilePin(IProfileRegistry registry, ICurrentProfile current, HouseholdLog household)
    {
        _registry = registry;
        _current = current;
        _household = household;
    }

    public async Task ExecuteAsync(string profileId, string? pin, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (profileId != _current.Id) ProfileAdministration.RequireAdmin(_current);
        ProfileAdministration.RequireProfile(_registry, profileId);
        _registry.SetPin(profileId, pin);
        await _household.AppendAsync([ProfileAdministration.Pin(profileId, _registry.GetPinHash(profileId))],
            cancellationToken);
    }
}

// At every start: records in the household what profiles.json holds and
// the household does not, i.e. what changed without a use case — the
// first profile made by the first-run wizard, the V1 migration, a PIN or
// role edited by hand. A profile missing from profiles.json is not
// recorded as removed: an unreadable file reads as empty, and only
// DeleteProfile removes a profile from the household.
//
// Step H2b: the installation settings too (an import restores the
// settings files, and a first start after the upgrade has none in the
// household yet). The stored SMTP password is compared in clear with the
// household's, which is protected locally.
public sealed class ReconcileHousehold
{
    private readonly IProfileRegistry _registry;
    private readonly HouseholdLog _household;
    private readonly IInstallationSettingsStore? _settings;
    private readonly ISmtpCredentialStore? _credentials;
    private readonly ICredentialProtector? _protector;

    public ReconcileHousehold(IProfileRegistry registry, HouseholdLog household,
        IInstallationSettingsStore? settings = null, ISmtpCredentialStore? credentials = null,
        ICredentialProtector? protector = null)
    {
        _registry = registry;
        _household = household;
        _settings = settings;
        _credentials = credentials;
        _protector = protector;
    }

    // Returns the number of operations recorded.
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var operations = await ProfileChangesAsync(cancellationToken);
        operations.AddRange(await SettingChangesAsync(cancellationToken));
        await _household.AppendAsync(operations, cancellationToken);
        return operations.Count;
    }

    private async Task<List<HouseholdOperationBody>> ProfileChangesAsync(CancellationToken cancellationToken)
    {
        var held = (await _household.ProfilesAsync(cancellationToken)).ToDictionary(p => p.ProfileId, StringComparer.Ordinal);
        var operations = new List<HouseholdOperationBody>();
        foreach (var profile in _registry.ListProfiles())
        {
            var role = ProfileAdministration.Wire(profile.Role);
            var hash = _registry.GetPinHash(profile.Id);
            var pin = HouseholdRegisters.PinValue(hash?.Hash, hash?.Salt, hash?.Iterations ?? 0);
            if (!held.TryGetValue(profile.Id, out var known))
            {
                operations.Add(new ProfileRegistered(profile.Id, profile.DisplayName, role, profile.CreatedAt));
                if (pin is not null) operations.Add(ProfileAdministration.Pin(profile.Id, hash));
                continue;
            }
            if (!string.Equals(known.DisplayName, profile.DisplayName, StringComparison.Ordinal))
                operations.Add(new ProfileRenamed(profile.Id, profile.DisplayName));
            if (!string.Equals(known.Role, role, StringComparison.Ordinal))
                operations.Add(new ProfileRoleChanged(profile.Id, role));
            if (!string.Equals(known.Pin, pin, StringComparison.Ordinal))
                operations.Add(ProfileAdministration.Pin(profile.Id, hash));
        }
        return operations;
    }

    private async Task<List<HouseholdOperationBody>> SettingChangesAsync(CancellationToken cancellationToken)
    {
        if (_settings is null) return [];
        var held = await _household.SettingsAsync(cancellationToken);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var map in new[]
                 {
                     InstallationSettingsMap.Of(_settings.ReadSmtp()),
                     InstallationSettingsMap.Of(_settings.ReadBackup()),
                     InstallationSettingsMap.Of(_settings.ReadUser()),
                 })
        {
            foreach (var (key, value) in map) values[key] = value;
        }
        List<HouseholdOperationBody> operations = [.. InstallationSettingsMap.Changes(values, held)];

        if (_credentials is not null && _protector is not null)
        {
            var stored = _credentials.GetPassword();
            held.TryGetValue(HouseholdSetting.SmtpPassword, out var recorded);
            if (!string.Equals(stored, Reveal(recorded), StringComparison.Ordinal)
                && !(stored is null && !held.ContainsKey(HouseholdSetting.SmtpPassword)))
            {
                operations.Add(new HouseholdSettingChanged(HouseholdSetting.SmtpPassword,
                    stored is null ? null : _protector.Protect(stored)));
            }
        }
        return operations;
    }

    // The recorded password in clear; null when there is none or it cannot
    // be read on this device (then it is recorded again).
    private string? Reveal(string? recorded)
    {
        if (string.IsNullOrEmpty(recorded)) return null;
        try
        {
            return _protector!.Unprotect(recorded);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
