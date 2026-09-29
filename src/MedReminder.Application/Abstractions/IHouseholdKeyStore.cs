namespace MedReminder.Application.Abstractions;

// The household key of this device (household feature, step H3a), kept
// protected on the device (DPAPI on Windows) under
// %LOCALAPPDATA%\MedReminder\household\household.protected.
public interface IHouseholdKeyStore
{
    // Null when no key of that version is stored.
    byte[]? Load(Guid householdId, int keyVersion);

    void Save(Guid householdId, int keyVersion, byte[] key);

    void Clear();
}
