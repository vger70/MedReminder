using MedReminder.Application.Abstractions;
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
    private byte[] _roi = [];
    private byte[] _mirroredRoi = [];
    private byte[] _enhancedRoi = [];
    private byte[] _grayRoi = [];
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
    public CameraFrameDiagnostics? LastDiagnostics { get; private set; }

    public RawBarcode? Decode(byte[] bgra32, int width, int height)
    {
        var source = new RGBLuminanceSource(bgra32, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var result = _reader.Decode(source);
        var (left, top, roiWidth, roiHeight) = GetScanRegion(width, height);
        var roiLength = roiWidth * roiHeight * 4;
        if (_roi.Length != roiLength) _roi = new byte[roiLength];
        if (_enhancedRoi.Length != roiLength) _enhancedRoi = new byte[roiLength];
        if (_mirroredRoi.Length != roiLength) _mirroredRoi = new byte[roiLength];
        if (_grayRoi.Length != roiLength / 4) _grayRoi = new byte[roiLength / 4];
        ExtractRegion(bgra32, width, left, top, roiWidth, roiHeight, _roi);
        LastDiagnostics = MeasureQuality(_roi, roiWidth, roiHeight, width, height);

        // The preview guide marks this central region. Trying it as a
        // fallback gives the decoder more scan lines over a small code
        // without losing coverage of the full frame above.
        if (result is null)
            result = _reader.Decode(new RGBLuminanceSource(_roi, roiWidth, roiHeight, RGBLuminanceSource.BitmapFormat.BGRA32));

        var enhanced = result is null && TryEnhanceContrast(roiWidth, roiHeight);
        if (enhanced)
            result = _reader.Decode(new RGBLuminanceSource(_enhancedRoi, roiWidth, roiHeight, RGBLuminanceSource.BitmapFormat.BGRA32));

        // Some camera drivers expose a horizontally mirrored preview
        // stream. Decode the original first (the common case), then try
        // mirrored full-frame and guide-region copies before giving up.
        if (result is null)
        {
            if (_mirrored.Length != bgra32.Length) _mirrored = new byte[bgra32.Length];
            Mirror(bgra32, width, height, _mirrored);
            result = _reader.Decode(new RGBLuminanceSource(_mirrored, width, height, RGBLuminanceSource.BitmapFormat.BGRA32));
        }
        if (result is null)
        {
            if (_mirroredRoi.Length != _roi.Length) _mirroredRoi = new byte[_roi.Length];
            Mirror(_roi, roiWidth, roiHeight, _mirroredRoi);
            result = _reader.Decode(new RGBLuminanceSource(_mirroredRoi, roiWidth, roiHeight, RGBLuminanceSource.BitmapFormat.BGRA32));
        }
        if (result is null && enhanced)
        {
            Mirror(_enhancedRoi, roiWidth, roiHeight, _mirroredRoi);
            result = _reader.Decode(new RGBLuminanceSource(_mirroredRoi, roiWidth, roiHeight, RGBLuminanceSource.BitmapFormat.BGRA32));
        }
        if (result is null || string.IsNullOrEmpty(result.Text)) return null;

        var symbology = Map(result.BarcodeFormat);
        return symbology is null ? null : new RawBarcode(symbology.Value, result.Text);
    }

    private static (int Left, int Top, int Width, int Height) GetScanRegion(int width, int height)
    {
        var regionWidth = Math.Max(1, width * 70 / 100);
        var regionHeight = Math.Max(1, height * 45 / 100);
        return ((width - regionWidth) / 2, (height - regionHeight) / 2, regionWidth, regionHeight);
    }

    private static void ExtractRegion(byte[] source, int sourceWidth, int left, int top, int width, int height, byte[] target)
    {
        var rowLength = width * 4;
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(source, ((top + y) * sourceWidth + left) * 4, target, y * rowLength, rowLength);
    }

    private static void Mirror(byte[] source, int width, int height, byte[] target)
    {
        var rowLength = width * 4;
        for (var y = 0; y < height; y++)
        {
            var row = y * rowLength;
            for (var x = 0; x < width; x++)
                Buffer.BlockCopy(source, row + x * 4, target, row + (width - 1 - x) * 4, 4);
        }
    }

    private bool TryEnhanceContrast(int width, int height)
    {
        var histogram = new int[256];
        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            var luminance = (byte)((77 * _roi[offset + 2] + 150 * _roi[offset + 1] + 29 * _roi[offset]) >> 8);
            _grayRoi[i] = luminance;
            histogram[luminance]++;
        }

        var low = Percentile(histogram, count, 2);
        var high = Percentile(histogram, count, 98);
        if (high - low < 8) return false;

        var range = high - low;
        for (var i = 0; i < count; i++)
        {
            var value = Math.Clamp((_grayRoi[i] - low) * 255 / range, 0, 255);
            var offset = i * 4;
            _enhancedRoi[offset] = (byte)value;
            _enhancedRoi[offset + 1] = (byte)value;
            _enhancedRoi[offset + 2] = (byte)value;
            _enhancedRoi[offset + 3] = 255;
        }
        return true;
    }

    private CameraFrameDiagnostics MeasureQuality(byte[] pixels, int width, int height, int frameWidth, int frameHeight)
    {
        var histogram = new int[256];
        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            var luminance = (byte)((77 * pixels[offset + 2] + 150 * pixels[offset + 1] + 29 * pixels[offset]) >> 8);
            _grayRoi[i] = luminance;
            histogram[luminance]++;
        }

        var contrast = Percentile(histogram, count, 95) - Percentile(histogram, count, 5);
        double sum = 0;
        double sumSquares = 0;
        var samples = 0;
        for (var y = 1; y < height - 1; y += 2)
        for (var x = 1; x < width - 1; x += 2)
        {
            var index = y * width + x;
            var laplacian = 4 * _grayRoi[index]
                - _grayRoi[index - 1] - _grayRoi[index + 1]
                - _grayRoi[index - width] - _grayRoi[index + width];
            sum += laplacian;
            sumSquares += laplacian * laplacian;
            samples++;
        }

        var mean = samples == 0 ? 0 : sum / samples;
        var sharpness = samples == 0 ? 0 : Math.Max(0, sumSquares / samples - mean * mean);
        var threshold = (Percentile(histogram, count, 5) + Percentile(histogram, count, 95)) / 2;
        (int Transitions, int Span) strongest = (0, 0);
        for (var line = 1; line <= 7; line++)
        {
            var row = Math.Min(height - 1, line * height / 8);
            strongest = Better(strongest, MeasureLine(_grayRoi, row * width, width, 1, threshold));
            var column = Math.Min(width - 1, line * width / 8);
            strongest = Better(strongest, MeasureLine(_grayRoi, column, height, width, threshold));
        }

        return new CameraFrameDiagnostics(frameWidth, frameHeight, width, height, contrast, sharpness, strongest.Transitions, strongest.Span);
    }

    private static (int Transitions, int Span) MeasureLine(byte[] gray, int offset, int length, int stride, int threshold)
    {
        var transitions = 0;
        var first = -1;
        var last = -1;
        var previous = gray[offset] < threshold;
        for (var i = 1; i < length; i++)
        {
            var current = gray[offset + i * stride] < threshold;
            if (current == previous) continue;
            transitions++;
            if (first < 0) first = i;
            last = i;
            previous = current;
        }
        return (transitions, first < 0 ? 0 : last - first);
    }

    private static (int Transitions, int Span) Better((int Transitions, int Span) current, (int Transitions, int Span) candidate) =>
        candidate.Transitions > current.Transitions ? candidate : current;

    private static int Percentile(int[] histogram, int count, int percentile)
    {
        var target = count * percentile / 100;
        var accumulated = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            accumulated += histogram[value];
            if (accumulated >= target) return value;
        }
        return 255;
    }

    internal static BarcodeSymbology? Map(BarcodeFormat format) => format switch
    {
        BarcodeFormat.DATA_MATRIX => BarcodeSymbology.DataMatrix,
        BarcodeFormat.CODE_39 => BarcodeSymbology.Code39,
        BarcodeFormat.EAN_13 => BarcodeSymbology.Ean13,
        _ => null,
    };
}
