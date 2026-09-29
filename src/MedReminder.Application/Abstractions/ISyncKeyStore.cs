namespace MedReminder.Application.Abstractions;

// The unwrapped group key of the current profile, kept on this device
// only (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.4): DPAPI CurrentUser
// on desktop (profiles\<id>\sync.protected), platform secure storage on
// mobile. Never logged, never written to the remote storage unwrapped.
public interface ISyncKeyStore
{
    // Null when no key of that version is stored.
    byte[]? Load(Guid groupId, int keyVersion);

    void Save(Guid groupId, int keyVersion, byte[] key);

    void Clear();
}
