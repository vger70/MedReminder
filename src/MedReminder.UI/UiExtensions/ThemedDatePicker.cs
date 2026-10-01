using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MedReminder.UI.UiExtensions;

// Dark field for date and time pickers (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
// §6c, S2). The native control paints a white field whatever the colour
// mode, and WinForms cannot recolour it. The painter lets the control
// paint itself into an off-screen bitmap, as it would on screen, and maps
// it onto the palette before the bitmap reaches the window: by brightness,
// white becomes the field colour, black the text colour, the shades
// between follow, including the coloured ClearType fringes of the text.
// The selected part of the date (system highlight colour, with its white
// text) is left as painted, so editing looks as before. The drop-down
// calendar is a separate window and stays light.
internal sealed class ThemedDatePicker : NativeWindow
{
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;

    // A pixel this close to the system highlight colour (sum of the
    // channel differences) belongs to the selected part of the date.
    private const int HighlightDistance = 60;
    private const int ColorHighlight = 13;

    private static readonly ConditionalWeakTable<DateTimePicker, ThemedDatePicker> Attached = new();

    private readonly DateTimePicker _picker;

    private ThemedDatePicker(DateTimePicker picker)
    {
        _picker = picker;
        picker.HandleCreated += (_, _) => Assign();
        picker.HandleDestroyed += (_, _) => ReleaseHandle();
        if (picker.IsHandleCreated) Assign();
    }

    public static void Attach(DateTimePicker picker)
        => Attached.GetValue(picker, p => new ThemedDatePicker(p));

    private void Assign()
    {
        if (Handle != IntPtr.Zero) ReleaseHandle();
        AssignHandle(_picker.Handle);
        _picker.Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmEraseBackground:
                // The paint below covers the whole client area.
                m.Result = 1;
                return;
            case WmPaint when m.WParam == IntPtr.Zero:
                PaintRecoloured(ref m);
                return;
        }
        base.WndProc(ref m);
    }

    private void PaintRecoloured(ref Message m)
    {
        var paint = default(PaintStruct);
        var screen = BeginPaint(Handle, ref paint);
        try
        {
            var size = _picker.ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                var memory = g.GetHdc();
                try
                {
                    // Common controls paint into the device context passed
                    // in wParam instead of calling BeginPaint themselves.
                    var offscreen = Message.Create(Handle, WmPaint, memory, IntPtr.Zero);
                    base.WndProc(ref offscreen);
                }
                finally
                {
                    g.ReleaseHdc(memory);
                }
            }
            Recolour(bitmap, UiTheme.Palette.Surface, UiTheme.Palette.Text, NativeHighlight());
            using var target = Graphics.FromHdc(screen);
            target.DrawImageUnscaled(bitmap, 0, 0);
        }
        finally
        {
            EndPaint(Handle, ref paint);
            m.Result = IntPtr.Zero;
        }
    }

    // The colour the native control selects with: Windows' own, not the
    // dark value WinForms reports through SystemColors.
    private static Color NativeHighlight()
    {
        var bgr = GetSysColor(ColorHighlight);
        return Color.FromArgb(bgr & 0xFF, (bgr >> 8) & 0xFF, (bgr >> 16) & 0xFF);
    }

    private static void Recolour(Bitmap bitmap, Color field, Color text, Color highlight)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride / 4;
            var pixels = new int[stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            // The selected field: the box around the highlight-coloured
            // pixels, kept as painted together with the text inside it.
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var c = pixels[y * stride + x];
                    var distance = Math.Abs(((c >> 16) & 0xFF) - highlight.R)
                        + Math.Abs(((c >> 8) & 0xFF) - highlight.G)
                        + Math.Abs((c & 0xFF) - highlight.B);
                    if (distance >= HighlightDistance) continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (x >= left && x <= right && y >= top && y <= bottom) continue;
                    var i = y * stride + x;
                    var c = pixels[i];
                    int b = c & 0xFF, g = (c >> 8) & 0xFF, r = (c >> 16) & 0xFF;
                    // Brightness 255 (white) -> field, 0 (black) -> text.
                    var t = (r * 299 + g * 587 + b * 114) / 1000;
                    var nr = text.R + (field.R - text.R) * t / 255;
                    var ng = text.G + (field.G - text.G) * t / 255;
                    var nb = text.B + (field.B - text.B) * t / 255;
                    pixels[i] = unchecked((int)0xFF000000) | (nr << 16) | (ng << 8) | nb;
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("user32.dll")]
    private static extern int GetSysColor(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, ref PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr hWnd, ref PaintStruct paint);
}
