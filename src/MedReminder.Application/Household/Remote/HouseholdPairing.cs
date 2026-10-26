using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Sync.Remote;

namespace MedReminder.Application.Household.Remote;

// The text of a household pairing code (household step H3c;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §6.2,
// docs/SYNC-FORMAT.md §9.5):
//
//   mrpair2.<householdId>.<deviceId>.<provider>.<secret>
//
// The parts are those of mrpair1 (SyncPairingCode), with the household id
// in place of the group id. As with mrpair1, the code holds no key: it
// names the offer file of the device that shows it and holds the secret
// that opens it.
public sealed record HouseholdPairingCode(Guid HouseholdId, Guid DeviceId, CloudProvider? Provider, byte[] Secret)
{
    public const string Prefix = "mrpair2";

    // Secret: never logged.
    public string Text => SyncPairingCode.Format(Prefix, HouseholdId, DeviceId, Provider, Secret);

    public override string ToString() => $"Household pairing code of device {DeviceId:N}";

    public static bool TryParse(string? text, out HouseholdPairingCode? code)
    {
        code = null;
        if (!SyncPairingCode.TryParse(text, Prefix, out var householdId, out var deviceId, out var provider, out var secret))
            return false;
        code = new HouseholdPairingCode(householdId, deviceId, provider, secret);
        return true;
    }
}

// A profile offered with a household pairing code: its group key.
public sealed record HouseholdOfferedProfile(string ProfileId, Guid GroupId, int KeyVersion, byte[] Key);

// What an offer holds once opened. The caller zeroes the keys.
public sealed record HouseholdOffer(int KeyVersion, byte[] Key, IReadOnlyList<HouseholdOfferedProfile> Profiles);

// <householdId>/pairing/<deviceId>.mrp: the household key and the group
// keys of the profiles an admin selected, encrypted (AES-256-GCM) with the
// secret of the code; associated data
// "MedReminder.Household.Pairing|<householdId N>|<deviceId N>". Written by
// the device that shows the code, deleted when the offer ends, refused
// after its expiry. The cleartext part holds ids and SecretCheck, a hash
// of the secret that tells a mistyped code from one a newer offer replaced
// (null in offers written before it).
public sealed record HouseholdPairingFile(int FormatVersion, Guid HouseholdId, Guid DeviceId, byte[] Nonce, byte[] Tag,
    byte[] Ciphertext, byte[]? SecretCheck = null)
{
    private sealed record Content(int KeyVersion, byte[] Key, IReadOnlyList<HouseholdOfferedProfile> Profiles,
        DateTimeOffset ExpiresAt);

    public static HouseholdPairingFile Seal(IArchiveCipher cipher, HouseholdPairingCode code, HouseholdOffer offer,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(offer);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            new Content(offer.KeyVersion, offer.Key, offer.Profiles, expiresAt), SyncFileCodec.Json);
        try
        {
            var (nonce, tag, ciphertext) = cipher.Encrypt(code.Secret, plaintext, Aad(code.HouseholdId, code.DeviceId));
            return new HouseholdPairingFile(SyncFileCodec.FormatVersion, code.HouseholdId, code.DeviceId, nonce, tag,
                ciphertext, Check(code.Secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    // HouseholdPairingMismatchException (a CryptographicException) when the
    // code does not open the file, SyncPairingExpiredException when the
    // offer is over.
    public HouseholdOffer Open(IArchiveCipher cipher, HouseholdPairingCode code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(code);
        if (FormatVersion > SyncFileCodec.FormatVersion)
            throw new NotSupportedException($"Pairing format {FormatVersion} is not supported.");
        if (HouseholdId != code.HouseholdId || DeviceId != code.DeviceId)
            throw new InvalidDataException("The pairing file does not match the code.");

        byte[] plaintext;
        try
        {
            plaintext = cipher.Decrypt(code.Secret, Nonce, Tag, Ciphertext, Aad(HouseholdId, DeviceId));
        }
        catch (CryptographicException ex)
        {
            throw Mismatch(code, ex);
        }
        try
        {
            var content = JsonSerializer.Deserialize<Content>(plaintext, SyncFileCodec.Json)
                ?? throw new InvalidDataException("Empty pairing file.");
            if (now > content.ExpiresAt)
            {
                CryptographicOperations.ZeroMemory(content.Key);
                foreach (var profile in content.Profiles) CryptographicOperations.ZeroMemory(profile.Key);
                throw new SyncPairingExpiredException();
            }
            return new HouseholdOffer(content.KeyVersion, content.Key, content.Profiles);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SyncFileCodec.Json);

    public static HouseholdPairingFile Parse(byte[] content)
        => JsonSerializer.Deserialize<HouseholdPairingFile>(content, SyncFileCodec.Json)
            ?? throw new InvalidDataException("Empty pairing file.");

    private static byte[] Aad(Guid householdId, Guid deviceId)
        => Encoding.UTF8.GetBytes($"MedReminder.Household.Pairing|{householdId:N}|{deviceId:N}");

    private static readonly byte[] CheckPrefix = Encoding.UTF8.GetBytes("MedReminder.Household.Pairing.Check|");

    // The secret holds 256 random bits: its hash gives nothing a guess could
    // not already test against the tag.
    private static byte[] Check(byte[] secret) => SHA256.HashData([.. CheckPrefix, .. secret]);

    // A secret one character away from the one the file was sealed with is a
    // mistyped code: each other character of the base64url alphabet is tried
    // at each position (about 2,700 hashes). Any other secret is a code a
    // newer offer replaced, or one with more than one wrong character.
    private HouseholdPairingMismatchException Mismatch(HouseholdPairingCode code, CryptographicException inner)
    {
        if (SecretCheck is null) return new HouseholdPairingMismatchException(HouseholdPairingMismatch.Unknown, null, inner);
        var text = code.Text;
        var start = text.LastIndexOf('.') + 1;
        var typed = text.ToCharArray();
        var candidate = new byte[code.Secret.Length];
        try
        {
            for (var i = start; i < typed.Length; i++)
            {
                var original = typed[i];
                foreach (var c in Base64UrlAlphabet)
                {
                    if (c == original) continue;
                    typed[i] = c;
                    if (Base64Url.IsValid(typed.AsSpan(start), out var length) && length == candidate.Length
                        && Base64Url.TryDecodeFromChars(typed.AsSpan(start), candidate, out var written)
                        && written == candidate.Length
                        && !candidate.AsSpan().SequenceEqual(code.Secret)
                        && CryptographicOperations.FixedTimeEquals(Check(candidate), SecretCheck))
                    {
                        return new HouseholdPairingMismatchException(HouseholdPairingMismatch.Mistyped, i + 1, inner);
                    }
                }
                typed[i] = original;
            }
            return new HouseholdPairingMismatchException(HouseholdPairingMismatch.Replaced, null, inner);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidate);
            Array.Clear(typed);
        }
    }

    private const string Base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
}

// Why a household pairing code did not open its offer.
public enum HouseholdPairingMismatch
{
    // The offer was written by a version that does not record SecretCheck.
    Unknown,

    // One character of the code is wrong: Position names it.
    Mistyped,

    // A newer offer replaced the one the code was made for, or more than one
    // character of the code is wrong.
    Replaced,
}

// Position: 1-based, in the code text, for Mistyped; null otherwise. A
// CryptographicException, as the hosts already catch for a code that does
// not open the offer.
public sealed class HouseholdPairingMismatchException(HouseholdPairingMismatch reason, int? position,
    Exception? inner = null) : CryptographicException($"The pairing code does not open the offer: {reason}.", inner)
{
    public HouseholdPairingMismatch Reason { get; } = reason;

    public int? Position { get; } = position;
}
