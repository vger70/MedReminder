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

// Role values on the wire, as in profiles.json.
public static class HouseholdRole
{
    public const string Admin = "admin";
    public const string User = "user";
}
