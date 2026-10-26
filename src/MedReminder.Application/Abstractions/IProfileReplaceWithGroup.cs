namespace MedReminder.Application.Abstractions;

// Household step H3d (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
// §6.4): after a join reported a profile kept on this device from before
// it left the installation (JoinedProfileStatus.Conflict), the user may
// replace that kept copy with the group granted by the installation. The
// group's database is built next to the profile database and swapped in;
// a restart follows. Implemented by the host, which builds the profile's
// services (the swap needs the profile's database), for any profile of
// this installation, open or not.
public interface IProfileReplaceWithGroup
{
    // InvalidOperationException when this device is not in a published
    // installation, the profile is not in it or not granted to this
    // device, or the profile already syncs with the granted group.
    Task ReplaceAsync(string profileId, string deviceName, CancellationToken cancellationToken);
}
