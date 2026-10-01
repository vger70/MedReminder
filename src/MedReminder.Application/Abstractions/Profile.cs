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
public sealed record ProfilePinHash(string Hash, string Salt, int Iterations)
{
    // Household step H3c: checks a PIN against a hash received from the
    // household, before the profile exists on this device (admin approval
    // of a join). Same derivation as ProfileRegistry.VerifyPin.
    public bool Matches(string pin)
    {
        if (string.IsNullOrEmpty(pin) || Iterations <= 0) return false;
        byte[] expected;
        byte[] salt;
        try
        {
            expected = Convert.FromBase64String(Hash);
            salt = Convert.FromBase64String(Salt);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256, expected.Length);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
