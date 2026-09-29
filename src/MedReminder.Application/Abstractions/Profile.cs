namespace MedReminder.Application.Abstractions;

// Immutable snapshot of a profile as exposed by IProfileRegistry.
// The PIN hash / salt are intentionally NOT exposed: callers only
// need to know whether a PIN is set (HasPin) and use VerifyPin() to
// check one — the raw material stays inside the registry.
//
// Id is generated with Guid.NewGuid().ToString("N") and is used both
// as the profiles.json key and as the folder name under
// %LOCALAPPDATA%\MedReminder\profiles\. It is immutable for the life
// of the profile; DisplayName can be renamed.
public sealed record Profile(
    string Id,
    string DisplayName,
    ProfileRole Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool HasPin);

// PBKDF2-HMAC-SHA256 hash and salt of a profile PIN, base64, as
// profiles.json stores them.
public sealed record ProfilePinHash(string Hash, string Salt, int Iterations);
