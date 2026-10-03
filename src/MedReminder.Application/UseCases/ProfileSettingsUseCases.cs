using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Packages;
using MedReminder.Application.Sync;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.UseCases;

// Settings → Notifications (B.1, P8 of docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §2): the recipients of this profile's
// emails. Each changed address is a ProfileSettingChanged operation, so
// the other devices of the profile's sync group get it. An address
// that has no version in the group yet (a group created before the
// settings were replicated) is recorded on the first save too, even when
// no address changed, so the devices end up with the same three
// addresses. The caller validates
// the addresses.
public sealed class UpdateNotificationSettings
{
    private readonly IProfileSettingsStore _store;
    private readonly SyncRegisters _registers;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;

    public UpdateNotificationSettings(IProfileSettingsStore store, SyncRegisters registers, IOperationLog operations,
        IUnitOfWork uow)
    {
        _store = store;
        _registers = registers;
        _operations = operations;
        _uow = uow;
    }

    // caregiverEmails and caregiverDigest (docs/notes/
    // EVOLUTION-PROPOSALS-2.md §3.8), and the package expiry lead days
    // (ANALYSIS-PACKAGE-EXPIRY.md §4.6): null leaves the setting as it is.
    public Task ExecuteAsync(string toAddress, string caregiverAddress, string doctorAddress,
        CancellationToken cancellationToken, string? caregiverEmails = null, string? caregiverDigest = null,
        string? packageExpiryLeadDays = null, string? packageInUseLeadDays = null)
    {
        // A replicated value every device reads alike: refused here rather
        // than clamped differently later.
        if (packageExpiryLeadDays is not null && !PackageSettings.IsValidPrinted(packageExpiryLeadDays))
            throw new ArgumentException("The printed-expiry lead days are out of range.", nameof(packageExpiryLeadDays));
        if (packageInUseLeadDays is not null && !PackageSettings.IsValidInUse(packageInUseLeadDays))
            throw new ArgumentException("The in-use lead days are out of range.", nameof(packageInUseLeadDays));
        return ExecuteCoreAsync(toAddress, caregiverAddress, doctorAddress, cancellationToken, caregiverEmails,
            caregiverDigest, packageExpiryLeadDays, packageInUseLeadDays);
    }

    private Task ExecuteCoreAsync(string toAddress, string caregiverAddress, string doctorAddress,
        CancellationToken cancellationToken, string? caregiverEmails, string? caregiverDigest,
        string? packageExpiryLeadDays, string? packageInUseLeadDays)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var current = _store.Read();
            var wanted = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [ProfileSetting.ToAddress] = toAddress?.Trim() ?? string.Empty,
                [ProfileSetting.CaregiverAddress] = caregiverAddress?.Trim() ?? string.Empty,
                [ProfileSetting.DoctorAddress] = doctorAddress?.Trim() ?? string.Empty,
            };
            if (caregiverEmails is not null) wanted[ProfileSetting.CaregiverEmails] = caregiverEmails.Trim();
            if (caregiverDigest is not null) wanted[ProfileSetting.CaregiverDigest] = caregiverDigest.Trim();
            if (packageExpiryLeadDays is not null)
                wanted[ProfileSetting.PackageExpiryLeadDays] = packageExpiryLeadDays.Trim();
            if (packageInUseLeadDays is not null)
                wanted[ProfileSetting.PackageInUseLeadDays] = packageInUseLeadDays.Trim();
            var changes = wanted
                .Where(w => !string.Equals(current.GetValueOrDefault(w.Key) ?? string.Empty, w.Value, StringComparison.Ordinal))
                .ToDictionary(w => w.Key, w => w.Value, StringComparer.Ordinal);
            // An unchanged save still records the addresses the group has
            // no version of: otherwise a group created before the settings
            // were replicated never carries them to a device that joins.
            var recorded = new List<SyncOperationBody>();
            foreach (var (setting, value) in wanted)
            {
                if (changes.ContainsKey(setting)
                    || await _registers.WinnerAsync(ProfileSettingsProjection.Entity,
                        ProfileSettingsProjection.Register(setting), ct) is null)
                {
                    recorded.Add(new ProfileSettingChanged(setting, value));
                }
            }
            if (recorded.Count == 0) return true;
            // The log first: if the file write fails, the next sync run
            // projects the recorded value into it.
            await _operations.AppendAsync(recorded, ct);
            await _uow.SaveChangesAsync(ct);
            if (changes.Count > 0) _store.Write(changes);
            return true;
        }, cancellationToken);
}

// A profile synced with other devices is renamed only while it is open:
// the new name is an operation of its own database. Thrown when the
// administrator renames another profile that takes part in sync.
public sealed class SyncedProfileRenameException : Exception
{
    public SyncedProfileRenameException()
        : base("This profile is synced with other devices: open it to rename it.")
    {
    }
}

// Tools → Manage profiles → Rename (B.1, P8): the display name of the
// current profile is a replicated setting (ProfileSettingChanged); the
// name of another profile is local to this installation unless that
// profile takes part in sync, in which case it must be opened first.
public sealed class RenameProfile
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _current;
    private readonly ISyncProfileStatus _sync;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly HouseholdLog? _household;

    // household: the name is also a household register (step H2).
    public RenameProfile(IProfileRegistry registry, ICurrentProfile current, ISyncProfileStatus sync,
        IOperationLog operations, IUnitOfWork uow, HouseholdLog? household = null)
    {
        _household = household;
        _registry = registry;
        _current = current;
        _sync = sync;
        _operations = operations;
        _uow = uow;
    }

    public async Task ExecuteAsync(string profileId, string newDisplayName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newDisplayName);
        var before = _registry.GetById(profileId)?.DisplayName;
        if (profileId != _current.Id)
        {
            if (_sync.IsSyncEnabled(profileId)) throw new SyncedProfileRenameException();
            _registry.Rename(profileId, newDisplayName);
        }
        else
        {
            await WriteGate.RunExclusiveAsync(async ct =>
            {
                _registry.Rename(profileId, newDisplayName);
                var renamed = _registry.GetById(profileId)?.DisplayName ?? newDisplayName.Trim();
                if (string.Equals(before, renamed, StringComparison.Ordinal)) return true;
                await _operations.AppendAsync([new ProfileSettingChanged(ProfileSetting.DisplayName, renamed)], ct);
                await _uow.SaveChangesAsync(ct);
                return true;
            }, cancellationToken);
        }

        var after = _registry.GetById(profileId)?.DisplayName ?? newDisplayName.Trim();
        if (_household is not null && !string.Equals(before, after, StringComparison.Ordinal))
        {
            await _household.AppendAsync([new ProfileRenamed(profileId, after)], cancellationToken);
        }
    }
}
