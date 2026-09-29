using System.Globalization;
using System.Text;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Default IBarcodeParser. Recognizes, in this order for payloads of
// unknown symbology:
//   1. GS1 DataMatrix (EU FMD unique identifier) -> GTIN (+ batch,
//      expiry, serial when unambiguous);
//   2. Code 32 / AIC (raw 6 characters, "A" + 9 digits, 9 digits)
//      -> national code;
//   3. EAN-13 -> GTIN.
// The shapes do not overlap (length and alphabet differ), so the
// order only matters for readability.
//
// Never derives an AIC from a GTIN: no verified public rule covers
// every AIC range (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §2.3).
public sealed class BarcodeParser : IBarcodeParser
{
    public const char GroupSeparator = '\u001D';

    // Visible stand-in for the group separator, used by the scanner
    // input box so the user sees where a separator was received.
    public const char GroupSeparatorGlyph = '␝';

    private const int MaxPayloadLength = 256;
    private const int MaxVariableFieldLength = 20;

    private readonly char? _groupSeparatorSubstitute;

    public BarcodeParser()
        : this(groupSeparatorSubstitute: null)
    {
    }

    public BarcodeParser(char? groupSeparatorSubstitute)
    {
        _groupSeparatorSubstitute = groupSeparatorSubstitute;
    }

    public static BarcodeParser FromOptions(BarcodeCaptureOptions? options)
    {
        var substitute = options?.HidGroupSeparatorSubstitute;
        return new BarcodeParser(string.IsNullOrEmpty(substitute) ? null : substitute[0]);
    }

    public BarcodeContent Parse(RawBarcode raw)
    {
        if (string.IsNullOrEmpty(raw.Payload) || raw.Payload.Length > MaxPayloadLength)
        {
            return BarcodeContent.Unrecognized;
        }

        var payload = Normalize(raw.Payload);
        var symbology = StripSymbologyIdentifier(ref payload, raw.Symbology);
        if (payload.Length == 0) return BarcodeContent.Unrecognized;

        return symbology switch
        {
            BarcodeSymbology.DataMatrix => ParseGs1(payload) ?? BarcodeContent.Unrecognized,
            BarcodeSymbology.Code39 => ParseAic(payload, allowBareDigits: true) ?? BarcodeContent.Unrecognized,
            BarcodeSymbology.Ean13 => ParseEan13(payload) ?? BarcodeContent.Unrecognized,
            _ => ParseGs1(payload)
                ?? ParseAic(payload, allowBareDigits: true)
                ?? ParseEan13(payload)
                ?? BarcodeContent.Unrecognized,
        };
    }

    // Trims scanner suffixes and whitespace, and maps both the visible
    // glyph and the configured substitute back to 0x1D.
    private string Normalize(string payload)
    {
        var sb = new StringBuilder(payload.Length);
        foreach (var c in payload)
        {
            if (c == GroupSeparatorGlyph || (_groupSeparatorSubstitute is { } s && c == s))
            {
                sb.Append(GroupSeparator);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim(' ', '\t', '\r', '\n');
    }

    // Scanners can be configured to prefix the AIM symbology
    // identifier ("]" + code character + modifier). When present it
    // is authoritative for the symbology and is removed.
    private static BarcodeSymbology StripSymbologyIdentifier(ref string payload, BarcodeSymbology reported)
    {
        if (payload.Length < 3 || payload[0] != ']') return reported;

        var detected = payload[1] switch
        {
            'd' => BarcodeSymbology.DataMatrix,
            'A' => BarcodeSymbology.Code39,
            'E' => BarcodeSymbology.Ean13,
            _ => BarcodeSymbology.Unknown,
        };
        if (detected == BarcodeSymbology.Unknown) return reported;

        payload = payload[3..];
        return detected;
    }

    // ---------------- GS1 element strings ----------------

    private static BarcodeContent? ParseGs1(string payload)
    {
        // AI 01 must come first: it is fixed-length, so its position is
        // unambiguous even when the scanner dropped every separator.
        if (payload.Length < 16 || !payload.StartsWith("01", StringComparison.Ordinal)) return null;
        var gtin = payload.Substring(2, 14);
        if (!IsDigits(gtin) || !HasValidGs1CheckDigit(gtin)) return null;

        // Without any separator in the payload, the end of a
        // variable-length element is only known when it is the last
        // one — and that cannot be told from the data. Fixed-length
        // elements before the first variable one are still safe.
        var separatorsPresent = payload.Contains(GroupSeparator);

        string? batch = null;
        string? serial = null;
        DateOnly? expiry = null;

        var pos = 16;
        while (pos < payload.Length)
        {
            if (payload[pos] == GroupSeparator)
            {
                pos++;
                continue;
            }
            if (pos + 2 > payload.Length) break;

            var ai = payload.Substring(pos, 2);
            if (ai == "17")
            {
                if (pos + 8 > payload.Length) break;
                expiry = ParseGs1Date(payload.AsSpan(pos + 2, 6));
                pos += 8;
            }
            else if (ai is "10" or "21")
            {
                if (!separatorsPresent) break;
                var start = pos + 2;
                var end = payload.IndexOf(GroupSeparator, start);
                if (end < 0) end = payload.Length;
                var value = payload[start..end];
                if (value.Length is 0 or > MaxVariableFieldLength) break;
                if (ai == "10") batch = value; else serial = value;
                pos = end;
            }
            else
            {
                // Unknown AI: its length is unknown, stop conservatively.
                break;
            }
        }

        return new BarcodeContent(gtin, NationalCode: null, batch, expiry, serial);
    }

    // YYMMDD. DD = 00 means "last day of the month" (GS1 General
    // Specifications). The century is fixed to 20xx: expiry dates of
    // medicines on the market are never in the 1900s.
    private static DateOnly? ParseGs1Date(ReadOnlySpan<char> yymmdd)
    {
        if (!IsDigits(yymmdd)) return null;
        var year = 2000 + int.Parse(yymmdd[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(yymmdd.Slice(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(yymmdd.Slice(4, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return null;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        if (day == 0) day = daysInMonth;
        if (day > daysInMonth) return null;
        return new DateOnly(year, month, day);
    }

    // ---------------- Code 32 / AIC ----------------

    private static BarcodeContent? ParseAic(string payload, bool allowBareDigits)
    {
        string? aic = null;

        if (payload.Length == ItalianPharmacode.Code32Length)
        {
            aic = ItalianPharmacode.TryDecodeCode32(payload);
        }
        else if (payload.Length == ItalianPharmacode.AicLength + 1
            && (payload[0] == 'A' || payload[0] == 'a'))
        {
            var digits = payload.AsSpan(1);
            if (ItalianPharmacode.IsValidAic(digits)) aic = digits.ToString();
        }
        else if (allowBareDigits && payload.Length == ItalianPharmacode.AicLength)
        {
            if (ItalianPharmacode.IsValidAic(payload)) aic = payload;
        }

        return aic is null ? null : new BarcodeContent(null, aic, null, null, null);
    }

    // ---------------- EAN-13 ----------------

    private static BarcodeContent? ParseEan13(string payload)
    {
        if (payload.Length != 13 || !IsDigits(payload) || !HasValidGs1CheckDigit(payload)) return null;
        return new BarcodeContent("0" + payload, NationalCode: null, null, null, null);
    }

    // ---------------- helpers ----------------

    // GS1 mod-10: weights 3 and 1 alternating from the rightmost data
    // digit (the one left of the check digit).
    private static bool HasValidGs1CheckDigit(string digits)
    {
        var sum = 0;
        var weight = 3;
        for (var i = digits.Length - 2; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }
        var check = (10 - (sum % 10)) % 10;
        return digits[^1] - '0' == check;
    }

    private static bool IsDigits(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return false;
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }
}
