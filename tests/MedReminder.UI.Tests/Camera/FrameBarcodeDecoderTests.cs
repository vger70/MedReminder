using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.UI.Camera;
using Xunit;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;

namespace MedReminder.UI.Tests.Camera;

// Renders each symbology found on Italian packs with the ZXing writer,
// feeds the BGRA pixels to the frame decoder as a camera frame would
// arrive, and checks that the parser gets a lookup key from the result.
// Camera optics are covered by the manual checklist
// (docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §9.5), not here.
public sealed class FrameBarcodeDecoderTests
{
    private const char Gs = '\u001D';

    [Fact]
    public void Code32_is_read_as_code39_and_parsed_to_the_aic()
    {
        var raw = Decode(BarcodeFormat.CODE_39, "0D4YD5", width: 480, height: 160);

        raw.Should().NotBeNull();
        raw!.Value.Symbology.Should().Be(BarcodeSymbology.Code39);
        Parse(raw.Value).NationalCode.Should().Be("012745093");
    }

    [Fact]
    public void Ean13_is_read_and_parsed_to_a_gtin()
    {
        var raw = Decode(BarcodeFormat.EAN_13, "8001234567897", width: 400, height: 160);

        raw.Should().NotBeNull();
        raw!.Value.Symbology.Should().Be(BarcodeSymbology.Ean13);
        Parse(raw.Value).Gtin.Should().Be("08001234567897");
    }

    [Fact]
    public void Gs1_datamatrix_is_read_and_parsed_to_a_gtin()
    {
        var options = new DatamatrixEncodingOptions { Width = 240, Height = 240, Margin = 4, GS1Format = true };
        var raw = Decode(BarcodeFormat.DATA_MATRIX, $"0108001234567897{Gs}21SERIAL123{Gs}1727123110LOT42", options);

        raw.Should().NotBeNull();
        raw!.Value.Symbology.Should().Be(BarcodeSymbology.DataMatrix);
        Parse(raw.Value).Gtin.Should().Be("08001234567897");
    }

    [Fact]
    public void Qr_code_on_the_packaging_is_ignored()
    {
        Decode(BarcodeFormat.QR_CODE, "https://example.org/leaflet", width: 240, height: 240)
            .Should().BeNull();
    }

    [Fact]
    public void Blank_frame_yields_nothing()
    {
        var pixels = new byte[320 * 240 * 4];
        Array.Fill(pixels, (byte)0xFF);

        new FrameBarcodeDecoder().Decode(pixels, 320, 240).Should().BeNull();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Mirrored_or_inverted_camera_frame_is_decoded(bool mirror, bool invert)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_39,
            Options = new EncodingOptions { Width = 480, Height = 160, Margin = 10 },
        };
        var image = writer.Write("0D4YD5");
        var pixels = (byte[])image.Pixels.Clone();
        if (mirror)
        {
            var rowBytes = image.Width * 4;
            var copy = (byte[])pixels.Clone();
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                    Buffer.BlockCopy(copy, y * rowBytes + x * 4, pixels, y * rowBytes + (image.Width - 1 - x) * 4, 4);
        }
        if (invert)
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)~pixels[i];
                pixels[i + 1] = (byte)~pixels[i + 1];
                pixels[i + 2] = (byte)~pixels[i + 2];
            }

        var raw = new FrameBarcodeDecoder().Decode(pixels, image.Width, image.Height);
        raw.Should().NotBeNull();
        Parse(raw!.Value).NationalCode.Should().Be("012745093");
    }

    [Theory]
    [InlineData(BarcodeFormat.DATA_MATRIX, BarcodeSymbology.DataMatrix)]
    [InlineData(BarcodeFormat.CODE_39, BarcodeSymbology.Code39)]
    [InlineData(BarcodeFormat.EAN_13, BarcodeSymbology.Ean13)]
    public void Pack_symbologies_are_mapped(BarcodeFormat format, BarcodeSymbology expected)
    {
        FrameBarcodeDecoder.Map(format).Should().Be(expected);
    }

    [Theory]
    [InlineData(BarcodeFormat.QR_CODE)]
    [InlineData(BarcodeFormat.CODE_128)]
    [InlineData(BarcodeFormat.EAN_8)]
    public void Other_symbologies_are_not_mapped(BarcodeFormat format)
    {
        FrameBarcodeDecoder.Map(format).Should().BeNull();
    }

    private static BarcodeContent Parse(RawBarcode raw) => new BarcodeParser().Parse(raw);

    private static RawBarcode? Decode(BarcodeFormat format, string contents, int width, int height) =>
        Decode(format, contents, new EncodingOptions { Width = width, Height = height, Margin = 10 });

    private static RawBarcode? Decode(BarcodeFormat format, string contents, EncodingOptions options)
    {
        var writer = new BarcodeWriterPixelData { Format = format, Options = options };
        // PixelData is 32-bit BGRA, top-down, without row padding: the
        // same layout as a camera frame converted to Bgra8.
        var image = writer.Write(contents);
        return new FrameBarcodeDecoder().Decode(image.Pixels, image.Width, image.Height);
    }
}
