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
