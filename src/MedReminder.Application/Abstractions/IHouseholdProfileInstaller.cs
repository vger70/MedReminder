namespace MedReminder.Application.Abstractions;

// A profile of the household brought to this device by an installation
// join (household step H3c; docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-
// DEVICE.md §6.3). Its database is built from the group image in a staging
// folder next to the profile folders, then the folder is moved into place
// in one step, so an interrupted join leaves no half-written profile.
public interface IHouseholdProfileInstaller
{
    // A new, empty staging folder, and the database path inside it.
    ProfileStaging CreateStaging();

    // Writes the sync settings and the group key into the staging folder,
    // then moves it to profiles\<profileId>\. IOException when that folder
    // exists. profiles.json is not touched (IProfileRegistry.Register).
    void Install(ProfileStaging staging, string profileId, SyncSettings settings, byte[] key);

    // Best effort.
    void Discard(ProfileStaging staging);
}

public sealed record ProfileStaging(string Directory, string DatabasePath);
