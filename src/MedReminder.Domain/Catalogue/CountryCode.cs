using System.Diagnostics.CodeAnalysis;

namespace MedReminder.Domain.Catalogue;

// Country identifier for reference-catalogue rows.
//
// Accepted values:
//  - ISO 3166-1 alpha-2 codes (e.g. "IT", "ES", "FR").
//  - "EU" for supranational (EMA centralised) authorisations.
//    The long form "European Union" is accepted on parsing and
//    normalised to "EU"; the DB never stores the long form.
public readonly record struct CountryCode
{
    public string Value { get; }

    private CountryCode(string value)
    {
        Value = value;
    }

    // True for the supranational EU pseudo-country used by EMA
    // Article 57 centralised authorisations.
    public bool IsSupranational => Value == "EU";

    public static CountryCode Parse(string raw)
    {
        if (!TryParse(raw, out var code))
        {
            throw new FormatException($"'{raw}' is not a valid country code.");
        }
        return code;
    }

    public static bool TryParse(string? raw, [NotNullWhen(true)] out CountryCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        // Normalise the supranational long form.
        if (string.Equals(trimmed, "European Union", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "EU", StringComparison.OrdinalIgnoreCase))
        {
            code = new CountryCode("EU");
            return true;
        }

        if (trimmed.Length != 2)
        {
            return false;
        }

        var upper = trimmed.ToUpperInvariant();
        for (var i = 0; i < upper.Length; i++)
        {
            if (upper[i] < 'A' || upper[i] > 'Z')
            {
                return false;
            }
        }

        code = new CountryCode(upper);
        return true;
    }

    public override string ToString() => Value;
}
