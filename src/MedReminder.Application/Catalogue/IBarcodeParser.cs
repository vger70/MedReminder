using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Turns a raw barcode payload into structured content. Pure and
// platform-independent: shared by every capture source (USB HID
// scanner today, webcam in a later phase). Never throws on malformed
// input — returns BarcodeContent.Unrecognized instead.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §3.3.
public interface IBarcodeParser
{
    BarcodeContent Parse(RawBarcode raw);
}
