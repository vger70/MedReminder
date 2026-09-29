namespace MedReminder.Application.Catalogue;

// Italian AIC code and its Code 32 barcode representation
// ("Italian Pharmacode", a Code 39 derivative).
//
// - AIC: 9 digits, the last one a check digit over the first eight
//   (odd positions weight 1, even positions weight 2 with the digits
//   of the product summed, total mod 10).
// - Code 32: the 9-digit AIC as a base-32 number, 6 characters, over
//   the alphabet 0-9 plus the consonants B..Z without the vowels
//   A, E, I, O.
//
// Both rules follow the BWIPP code32 encoder. The check-digit rule
// holds for all but 13 of the 300,193 AIC codes in the shipped AIFA
// snapshot (aifa-202609); those outliers are rejected.
public static class ItalianPharmacode
{
    public const int AicLength = 9;
    public const int Code32Length = 6;

    private const string Code32Alphabet = "0123456789BCDFGHJKLMNPQRSTUVWXYZ";

    public static bool IsValidAic(ReadOnlySpan<char> aic)
    {
        if (aic.Length != AicLength) return false;
        foreach (var c in aic)
        {
            if (c is < '0' or > '9') return false;
        }
        return aic[8] - '0' == ComputeCheckDigit(aic[..8]);
    }

    // Decodes the 6-character Code 32 form into the 9-digit AIC.
    // Returns null when a character is outside the alphabet, the value
    // exceeds 9 digits, or the check digit does not match.
    public static string? TryDecodeCode32(ReadOnlySpan<char> code32)
    {
        if (code32.Length != Code32Length) return null;

        long value = 0;
        foreach (var raw in code32)
        {
            var index = Code32Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (index < 0) return null;
            value = value * 32 + index;
        }

        if (value > 999_999_999) return null;
        var aic = value.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        return IsValidAic(aic) ? aic : null;
    }

    public static int ComputeCheckDigit(ReadOnlySpan<char> firstEightDigits)
    {
        var sum = 0;
        for (var i = 0; i < firstEightDigits.Length; i++)
        {
            var d = firstEightDigits[i] - '0';
            if (i % 2 != 0)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return sum % 10;
    }
}
