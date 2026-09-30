using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MedReminder.UI.UiExtensions;

// Draws the border of a text box or a number box in the palette's colours
// in dark mode (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §6c, S3).
// WinForms offers no border colour: in dark mode the stock border stays
// light next to the dark field. The painter listens to the control's
// window messages and draws over the border after Windows has drawn it,
// so no form changes its layout.
//
//  - Text boxes: the border is the non-client edge (WM_NCPAINT). The
//    outer pixel takes Border, or Accent while the box has the focus;
//    the inner pixel of the 3D edge takes the field colour.
//  - Number boxes (UpDownBase): the border is drawn in the client area
//    at the end of WM_PAINT, one pixel wide.
internal sealed class ThemedBorder : NativeWindow
{
    private const int WmPaint = 0x000F;
    private const int WmNcPaint = 0x0085;
    private const int WmSetFocus = 0x0007;
    private const int WmKillFocus = 0x0008;
    private const int WmEnable = 0x000A;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwFrame = 0x0400;
    private const uint RdwUpdateNow = 0x0100;

    // Keeps each painter alive as long as its control and prevents a
    // second one on the same control.
    private static readonly ConditionalWeakTable<Control, ThemedBorder> Attached = new();

    private readonly Control _control;
    private readonly bool _nonClient;

    private ThemedBorder(Control control, bool nonClient)
    {
        _control = control;
        _nonClient = nonClient;
        control.HandleCreated += (_, _) => Assign();
        control.HandleDestroyed += (_, _) => ReleaseHandle();
        if (control.IsHandleCreated) Assign();
    }

    public static void Attach(TextBoxBase textBox)
    {
        if (textBox.BorderStyle == BorderStyle.None) return;
        Attached.GetValue(textBox, c => new ThemedBorder(c, nonClient: true));
    }

    public static void Attach(UpDownBase upDown)
    {
        if (upDown.BorderStyle == BorderStyle.None) return;
        Attached.GetValue(upDown, c => new ThemedBorder(c, nonClient: false));
    }

    private void Assign()
    {
        if (Handle != IntPtr.Zero) ReleaseHandle();
        AssignHandle(_control.Handle);
        Redraw();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        switch (m.Msg)
        {
            case WmNcPaint when _nonClient:
                PaintNonClientBorder();
                break;
            case WmPaint when !_nonClient:
                PaintClientBorder();
                break;
            case WmSetFocus or WmKillFocus or WmEnable when _nonClient:
                Redraw();
                break;
        }
    }

    private void Redraw()
    {
        if (Handle == IntPtr.Zero) return;
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
            RdwInvalidate | RdwUpdateNow | (_nonClient ? RdwFrame : 0));
    }

    private void PaintNonClientBorder()
    {
        if (!GetWindowRect(Handle, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 2 || height < 2) return;

        var palette = UiTheme.Palette;
        var dc = GetWindowDC(Handle);
        if (dc == IntPtr.Zero) return;
        try
        {
            using var g = Graphics.FromHdc(dc);
            var outer = _control.Focused && _control.Enabled ? palette.Accent : palette.Border;
            using (var pen = new Pen(outer))
            {
                g.DrawRectangle(pen, 0, 0, width - 1, height - 1);
            }
            if (((TextBoxBase)_control).BorderStyle == BorderStyle.Fixed3D && width > 4 && height > 4)
            {
                using var inner = new Pen(_control.BackColor);
                g.DrawRectangle(inner, 1, 1, width - 3, height - 3);
            }
        }
        finally
        {
            ReleaseDC(Handle, dc);
        }
    }

    private void PaintClientBorder()
    {
        var size = _control.ClientSize;
        if (size.Width < 2 || size.Height < 2) return;
        var dc = GetDC(Handle);
        if (dc == IntPtr.Zero) return;
        try
        {
            using var g = Graphics.FromHdc(dc);
            using var pen = new Pen(UiTheme.Palette.Border);
            g.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
        }
        finally
        {
            ReleaseDC(Handle, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rectUpdate, IntPtr regionUpdate, uint flags);
}
