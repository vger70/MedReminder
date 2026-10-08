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
    private byte[] _mirrored = [];
    private readonly BarcodeReaderGeneric _reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            // Packaging can be glossy or use light bars on a dark field.
            TryInverted = true,
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
        // Some camera drivers expose a horizontally mirrored preview
        // stream. Decode the original first (the common case), then try
        // a mirrored copy before giving up.
        if (result is null)
        {
            if (_mirrored.Length != bgra32.Length) _mirrored = new byte[bgra32.Length];
            var rowLength = width * 4;
            for (var y = 0; y < height; y++)
            {
                var row = y * rowLength;
                for (var x = 0; x < width; x++)
                    Buffer.BlockCopy(bgra32, row + x * 4, _mirrored, row + (width - 1 - x) * 4, 4);
            }
            result = _reader.Decode(new RGBLuminanceSource(_mirrored, width, height, RGBLuminanceSource.BitmapFormat.BGRA32));
        }
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
