namespace MedReminder.Domain.Household;

// Operation catalogue of the household, schema version 1 (household
// feature, docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §5.2, step
// H2). The household is the installation: its profiles, their roles and
// PINs, and later its settings and devices. Until step H3 the household
// lives on one device and its operations are only recorded locally; the
// log gives every change an HLC timestamp so that H3 can replicate it
// without a format change.
//
// Every operation carries the profile it touches (the profile id of
// profiles.json: "default" or 32 hex digits). Wire names and field names
// are part of the format: renaming a type, a property or a role value is
// a format change.
public abstract record HouseholdOperationBody(string ProfileId);

// A profile of the installation, with its name and role when created.
public sealed record ProfileRegistered(
    string ProfileId,
    string DisplayName,
    string Role,
    DateTimeOffset CreatedAt) : HouseholdOperationBody(ProfileId);

public sealed record ProfileRenamed(
    string ProfileId,
    string DisplayName) : HouseholdOperationBody(ProfileId);

public sealed record ProfileRoleChanged(
    string ProfileId,
    string Role) : HouseholdOperationBody(ProfileId);

// The PIN hash as profiles.json stores it (PBKDF2-HMAC-SHA256, base64);
// Hash and Salt null and Iterations 0 when the PIN is cleared. The PIN
// itself never leaves the device that typed it.
public sealed record ProfilePinChanged(
    string ProfileId,
    string? Hash,
    string? Salt,
    int Iterations) : HouseholdOperationBody(ProfileId);

// Tombstone: wins over every other operation for the profile.
public sealed record ProfileRemoved(string ProfileId) : HouseholdOperationBody(ProfileId);

// A setting of the installation (step H2b): SMTP transport and password,
// scheduled cloud backup policy, reference country (HouseholdSetting).
// Not tied to a profile: ProfileId is HouseholdRegisters.Installation.
// A secret setting (HouseholdSetting.IsSecret) carries its value
// protected with the local credential protector (DPAPI on Windows) in
// the local log; the replication of step H3 carries it inside the
// encrypted segment instead. Null clears the setting.
public sealed record HouseholdSettingChanged(
    string Setting,
    string? Value) : HouseholdOperationBody(HouseholdRegisters.Installation);

// Step H3b: keys (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.4).
// Public keys are SubjectPublicKeyInfo of an ECDH P-256 key, base64. A
// wrapped key is the output of HouseholdKeyWrap: only the holder of the
// matching private key can open it.

// A device's public key, for the grants made to it.
public sealed record DeviceKeyPublished(Guid DeviceId, string PublicKey)
    : HouseholdOperationBody(HouseholdRegisters.DeviceEntity(DeviceId));

// The recovery public key; its private key is in recovery.<v>.wrap,
// wrapped with the household passphrase.
public sealed record RecoveryKeyPublished(int KeyVersion, string PublicKey)
    : HouseholdOperationBody(HouseholdRegisters.Recovery);

// The key of the profile's sync group, wrapped for one device.
public sealed record ProfileKeyGranted(
    string ProfileId,
    Guid DeviceId,
    Guid GroupId,
    int KeyVersion,
    string WrappedKey) : HouseholdOperationBody(ProfileId);

// The device no longer receives the profile's key; a rotation of the
// group key follows (step H5).
public sealed record ProfileKeyRevoked(string ProfileId, Guid DeviceId) : HouseholdOperationBody(ProfileId);

// The key of the profile's sync group, wrapped for the recovery key.
public sealed record ProfileKeyEscrowed(
    string ProfileId,
    Guid GroupId,
    int KeyVersion,
    string WrappedKey) : HouseholdOperationBody(ProfileId);

// Step H4a: the master device (docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §7). An administrator elects a device;
// the elected device activates the election when the outgoing master has
// released it or has not synced for longer than the lease and a margin
// (MasterRules). Only the active master sends email and runs the cloud
// backup. The last election by HLC wins; an activation counts only for the
// current election.
public sealed record MasterElected(Guid ElectionId, Guid DeviceId, string ElectedBy, string Kind)
    : HouseholdOperationBody(HouseholdRegisters.MasterEntity);

public sealed record MasterActivated(Guid ElectionId, Guid DeviceId)
    : HouseholdOperationBody(HouseholdRegisters.MasterEntity);

// Recorded by the active master when it applies an election naming another
// device: it has stopped, and the elected device may activate.
public sealed record MasterReleased(Guid ElectionId) : HouseholdOperationBody(HouseholdRegisters.MasterEntity);

// MasterElected.Kind on the wire.
public static class MasterElectionKind
{
    // The device that publishes the household (R1).
    public const string Creation = "Creation";
    public const string Planned = "Planned";
    public const string Takeover = "Takeover";
}

// Names of the installation settings (HouseholdSettingChanged.Setting).
// Part of the format. Values are invariant text: booleans "true" /
// "false", integers in decimal, the cloud provider by enum member name.
// Device-bound settings (backup folders, language, update check,
// auto-start) are not household settings.
public static class HouseholdSetting
{
    public const string SmtpHost = "Smtp.Host";
    public const string SmtpPort = "Smtp.Port";
    public const string SmtpUseStartTls = "Smtp.UseStartTls";
    public const string SmtpUsername = "Smtp.Username";
    public const string SmtpFromAddress = "Smtp.FromAddress";
    public const string SmtpFromDisplayName = "Smtp.FromDisplayName";
    public const string SmtpTimeoutSeconds = "Smtp.TimeoutSeconds";
    public const string SmtpPassword = "Smtp.Password";
    public const string CloudBackupEnabled = "CloudBackup.Enabled";
    public const string CloudBackupRetention = "CloudBackup.Retention";
    public const string CloudBackupProvider = "CloudBackup.Provider";
    public const string CloudBackupAccountId = "CloudBackup.AccountId";
    public const string ReferenceCountry = "ReferenceCountry";

    public static bool IsSecret(string setting) => setting == SmtpPassword;
}

// Role values on the wire, as in profiles.json.
public static class HouseholdRole
{
    public const string Admin = "admin";
    public const string User = "user";
}
