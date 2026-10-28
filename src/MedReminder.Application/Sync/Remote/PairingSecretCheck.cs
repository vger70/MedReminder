using System.Buffers.Text;
using System.Security.Cryptography;
using MedReminder.Application.Household.Remote;

namespace MedReminder.Application.Sync.Remote;

// The secret check of a pairing file (mrpair1 and mrpair2): a hash of the
// secret, kept in the file's cleartext, that tells a mistyped code from one
// a newer offer replaced. It is not authenticated and only words the error
// for a code that does not open the offer; it never opens one. Shared by
// SyncPairingFile and HouseholdPairingFile (issue #306).
internal static class PairingSecretCheck
{
    private const string Base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    // The secret holds 256 random bits: its hash gives nothing a guess could
    // not already test against the tag. Hashed incrementally: no copy of the
    // secret is left to zero.
    public static byte[] Compute(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> secret)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        hash.AppendData(secret);
        return hash.GetHashAndReset();
    }

    // Why `secret` does not open a file sealed with the secret `check` was
    // made from. A secret one typing mistake away is a mistyped code: each
    // other character of the base64url alphabet at each position, and each
    // swap of two neighbouring characters (about 2,700 hashes). Any other
    // secret is a code a newer offer replaced, or one with more than one
    // mistake. Position is 1-based within the secret, the only part the user
    // copies. Without a check (a file written before it): Unknown.
    //
    // Works on the secret's base64url characters alone, never on the code's
    // text: that would copy the secret into an immutable string this method
    // could not zero.
    public static (HouseholdPairingMismatch Reason, int? Position) Diagnose(ReadOnlySpan<byte> prefix, byte[] secret,
        byte[]? check)
    {
        if (check is null) return (HouseholdPairingMismatch.Unknown, null);
        var typed = new char[Base64Url.GetEncodedLength(secret.Length)];
        Base64Url.EncodeToChars(secret, typed);
        var candidate = new byte[secret.Length];
        try
        {
            for (var i = 0; i < typed.Length; i++)
            {
                var original = typed[i];
                foreach (var c in Base64UrlAlphabet)
                {
                    if (c == original) continue;
                    typed[i] = c;
                    if (Matches(prefix, typed, secret, candidate, check)) return (HouseholdPairingMismatch.Mistyped, i + 1);
                }
                typed[i] = original;
                if (i + 1 < typed.Length && typed[i + 1] != original)
                {
                    (typed[i], typed[i + 1]) = (typed[i + 1], original);
                    var swapped = Matches(prefix, typed, secret, candidate, check);
                    (typed[i], typed[i + 1]) = (original, typed[i]);
                    if (swapped) return (HouseholdPairingMismatch.Mistyped, i + 1);
                }
            }
            return (HouseholdPairingMismatch.Replaced, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
            Array.Clear(typed);
        }
    }

    // candidate: a buffer of the secret's size, overwritten.
    private static bool Matches(ReadOnlySpan<byte> prefix, ReadOnlySpan<char> secretText, byte[] typed, byte[] candidate,
        byte[] check)
        => Base64Url.IsValid(secretText, out var length) && length == candidate.Length
            && Base64Url.TryDecodeFromChars(secretText, candidate, out var written) && written == candidate.Length
            && !candidate.AsSpan().SequenceEqual(typed)
            && CryptographicOperations.FixedTimeEquals(Compute(prefix, candidate), check);
}
