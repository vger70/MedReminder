namespace MedReminder.Application.Abstractions;

// This device's key pair for the household (household step H3b): the
// private key protected on the device (DPAPI on Windows) in
// household\device.protected. It opens the profile keys granted to this
// device; its public key is household state (DeviceKeyPublished).
public interface IDeviceKeyStore
{
    // PKCS#8, base64; null when the device has none yet.
    string? LoadPrivateKey();

    void SavePrivateKey(string privateKey);
}

// The key of a profile's sync group on this installation (household step
// H3b): profiles\<id>\sync.settings.json and sync.protected, for any
// profile, not only the open one.
public interface IProfileGroupKeys
{
    // Null when the profile is not synced or its key is not stored here.
    ProfileGroupKey? Load(string profileId);
}

public sealed record ProfileGroupKey(Guid GroupId, int KeyVersion, byte[] Key);
