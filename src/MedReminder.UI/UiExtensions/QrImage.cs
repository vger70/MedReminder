using QRCoder;

namespace MedReminder.UI.UiExtensions;

// A QR code as a bitmap, for a phone camera to read: the sync pairing
// offer and the regional prescription service link.
internal static class QrImage
{
    public static Bitmap Render(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        using var stream = new MemoryStream(png.GetGraphic(8));
        // A copy: Image.FromStream needs its stream for the image's life.
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }
}
