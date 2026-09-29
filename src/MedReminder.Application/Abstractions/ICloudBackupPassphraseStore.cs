namespace MedReminder.Application.Abstractions;

// Vault for the C.3+ automatic-cloud-backup passphrase
// (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §3.4, §3.5). Distinct
// from the user-typed C.3 export passphrase: this one is DPAPI-encrypted
// on the local Windows account so the unattended daily tick can derive
// the archive key without prompting the user. The Infrastructure
// implementation serializes the DPAPI blob to
// %LOCALAPPDATA%\MedReminder\cloud-backup.protected — same folder tier
// as smtp.protected. Never logged, never leaves the machine.
public interface ICloudBackupPassphraseStore
{
    // True when a passphrase has been configured on this machine.
    // A false result means the daily tick must skip the cloud target
    // and log a warning (§4.6): the UI prompts on the next open of the
    // Cloud Backup settings dialog.
    bool HasPassphrase { get; }

    // Returns the passphrase in a caller-owned char[] buffer, or null
    // when HasPassphrase is false / the DPAPI decrypt fails. The caller
    // is responsible for zeroing the buffer once the derived archive
    // key is materialised.
    char[]? GetPassphrase();

    // Persists the passphrase, DPAPI-encrypted on the current Windows
    // account. The caller-supplied buffer is treated as read-only; the
    // caller must zero it after this call. Empty / whitespace-only
    // passphrases are rejected.
    void SetPassphrase(char[] passphrase);

    // Removes the on-disk cache; HasPassphrase becomes false. Idempotent.
    void Clear();
}
