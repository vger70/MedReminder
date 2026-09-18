using System.Diagnostics.CodeAnalysis;

namespace MedReminder.Domain.Catalogue;

// WHO Anatomical Therapeutic Chemical (ATC) code, at the 7-character
// chemical-substance level (e.g. "A10BA02" for metformin).
//
// Structure (WHO ATC): letter, digit, digit, letter, letter, digit, digit.
// Shorter administrative prefixes exist (A, A10, A10B, A10BA) but the
// catalogue only stores the full 7-char code — the shorter prefixes
// are derived from it when needed.
public readonly record struct AtcCode
{
    public string Value { get; }

    private AtcCode(string value)
    {
        Value = value;
    }

    public static AtcCode Parse(string raw)
    {
        if (!TryParse(raw, out var atc))
        {
            throw new FormatException($"'{raw}' is not a valid 7-character ATC code.");
        }
        return atc;
    }

    public static bool TryParse(string? raw, [NotNullWhen(true)] out AtcCode atc)
    {
        atc = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        if (trimmed.Length != 7)
        {
            return false;
        }

        if (!IsLetter(trimmed[0])
            || !IsDigit(trimmed[1])
            || !IsDigit(trimmed[2])
            || !IsLetter(trimmed[3])
            || !IsLetter(trimmed[4])
            || !IsDigit(trimmed[5])
            || !IsDigit(trimmed[6]))
        {
            return false;
        }

        atc = new AtcCode(trimmed);
        return true;
    }

    private static bool IsLetter(char c) => c >= 'A' && c <= 'Z';

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    public override string ToString() => Value;
}
