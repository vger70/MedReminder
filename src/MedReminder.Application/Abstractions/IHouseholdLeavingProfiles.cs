namespace MedReminder.Application.Abstractions;

// What a device does with a household profile it holds when it leaves the
// household (issue #88 of vger70/medreminder-mobile, LeaveHousehold). The
// host implements it: each profile has its own database and sync files,
// and only the host knows how to open a profile that is not the open one,
// or to remove one.
public interface IHouseholdLeavingProfiles
{
    // Sync turned off for the profile on this device: its sync settings,
    // group key and peers go, its data stays.
    Task KeepAsync(string profileId, CancellationToken cancellationToken);

    // The profile and its data removed from this device.
    Task DeleteAsync(string profileId, CancellationToken cancellationToken);
}
