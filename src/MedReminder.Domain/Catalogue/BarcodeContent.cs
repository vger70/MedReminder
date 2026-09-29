namespace MedReminder.Domain.Catalogue;

// Structured result of parsing a RawBarcode. Every field is optional:
// extraction is best-effort and a field is left null rather than
// guessed when the payload is ambiguous
// (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §2.1, §3.2).
public sealed record BarcodeContent(
    // GS1 GTIN, 14 digits (EAN-13 values are left-padded with a zero).
    string? Gtin,
    // National product code: the 9-digit AIC for Italian packages.
    string? NationalCode,
    // GS1 AI 10, only when its end is unambiguous.
    string? Batch,
    // GS1 AI 17, only when its position is unambiguous.
    DateOnly? Expiry,
    // GS1 AI 21, only when its end is unambiguous. Not used by A2.
    string? Serial)
{
    public static readonly BarcodeContent Unrecognized = new(null, null, null, null, null);

    // True when the content carries a key the catalogue can be
    // queried with. National code is preferred over GTIN.
    public bool HasLookupKey => NationalCode is not null || Gtin is not null;

    public string? LookupKey => NationalCode ?? Gtin;
}
