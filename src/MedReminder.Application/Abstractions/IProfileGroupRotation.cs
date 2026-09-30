namespace MedReminder.Application.Abstractions;

// Household step H5b (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
// §9 points 2 and 5): changes the key of a profile's sync group after a
// device removal, for any profile of this installation, open or not. The
// new key is wrapped with a random passphrase that is never shown: the
// devices get it through their household grants, recovery goes through
// the recovery key. Implemented by the host, which builds the profile's
// services (RotateSyncKey needs the profile's database).
public interface IProfileGroupRotation
{
    // The new group key; the caller zeroes it. InvalidOperationException
    // when the profile is not synced here or its group is behind.
    Task<ProfileGroupKey> RotateAsync(string profileId, CancellationToken cancellationToken);
}
