using MedReminder.Domain.Catalogue;
using ZXing;
using ZXing.Common;

namespace MedReminder.UI.Camera;

// Decodes one camera frame with ZXing.Net, restricted to the three
// symbologies found on medicine packs sold in Italy. Not thread-safe:
// one instance per scan session.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §6.
internal sealed class FrameBarcodeDecoder
{
    private readonly BarcodeReaderGeneric _reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = false,
            // Restricting the formats cuts decode time and ignores
            // marketing QR codes on the packaging. CODE_39 carries
            // Code 32; the parser converts it (§2.2).
            PossibleFormats = [BarcodeFormat.CODE_39, BarcodeFormat.EAN_13, BarcodeFormat.DATA_MATRIX],
        },
    };

    // Pixels are 32-bit BGRA, top-down, with no row padding
    // (stride = width * 4). Returns null when nothing is decoded.
    public RawBarcode? Decode(byte[] bgra32, int width, int height)
    {
        var source = new RGBLuminanceSource(bgra32, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var result = _reader.Decode(source);
        if (result is null || string.IsNullOrEmpty(result.Text)) return null;

        var symbology = Map(result.BarcodeFormat);
        return symbology is null ? null : new RawBarcode(symbology.Value, result.Text);
    }

    internal static BarcodeSymbology? Map(BarcodeFormat format) => format switch
    {
        BarcodeFormat.DATA_MATRIX => BarcodeSymbology.DataMatrix,
        BarcodeFormat.CODE_39 => BarcodeSymbology.Code39,
        BarcodeFormat.EAN_13 => BarcodeSymbology.Ean13,
        _ => null,
    };
}
