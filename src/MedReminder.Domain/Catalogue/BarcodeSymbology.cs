namespace MedReminder.Domain.Catalogue;

// Barcode symbology as reported by the capture source. A USB HID
// scanner in keyboard-wedge mode does not report it, so its payloads
// arrive as Unknown and the parser infers the shape from the content
// (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §3.1).
//
// Values are explicit because they are logged; never renumber them.
public enum BarcodeSymbology : int
{
    Unknown = 0,
    Ean13 = 1,
    DataMatrix = 2,
    // Carries Code 32 (Italian Pharmacode), a Code 39 derivative.
    Code39 = 3,
}
