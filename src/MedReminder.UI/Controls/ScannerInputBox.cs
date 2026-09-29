using MedReminder.Application.Catalogue;

namespace MedReminder.UI.Controls;

// Single-line box that receives the keystrokes of a USB HID barcode
// scanner in keyboard-wedge mode, and also accepts a code typed by
// hand. See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5B.2.
//
// - Enter, Tab, CR or LF end the payload and raise PayloadCompleted.
//   IsInputKey claims Enter / Tab so they reach this control instead
//   of triggering dialog navigation.
// - The GS1 group separator (0x1D) has no printable key. A wedge
//   scanner may type it as Ctrl+] (VK_OEM_6 on a US layout) or as the
//   raw control character; both are shown as the visible glyph
//   U+241D, which the parser maps back to 0x1D. Whether every
//   keyboard layout delivers the chord this way is not verified: the
//   configurable substitute character (BarcodeCaptureOptions) covers
//   the other cases.
internal sealed class ScannerInputBox : TextBox
{
    public const int MaxPayloadLength = 256;

    public event EventHandler<string>? PayloadCompleted;

    public ScannerInputBox()
    {
        Multiline = false;
        AcceptsReturn = false;
        AcceptsTab = false;
        MaxLength = MaxPayloadLength;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var modifiers = keyData & Keys.Modifiers;
        var key = keyData & Keys.KeyCode;
        if (modifiers == Keys.None && key is Keys.Enter or Keys.Tab) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Modifiers == Keys.None && e.KeyCode is Keys.Enter or Keys.Tab)
        {
            // SuppressKeyPress also avoids the edit control's beep.
            e.Handled = true;
            e.SuppressKeyPress = true;
            Complete();
            return;
        }
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.OemCloseBrackets)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            InsertGroupSeparator();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        switch (e.KeyChar)
        {
            case BarcodeParser.GroupSeparator:
                e.Handled = true;
                InsertGroupSeparator();
                return;
            case '\r':
            case '\n':
                e.Handled = true;
                Complete();
                return;
        }
        base.OnKeyPress(e);
    }

    private void InsertGroupSeparator()
    {
        if (TextLength - SelectionLength >= MaxLength) return;
        SelectedText = BarcodeParser.GroupSeparatorGlyph.ToString();
    }

    private void Complete()
    {
        var text = Text;
        if (text.Length == 0) return;
        PayloadCompleted?.Invoke(this, text);
    }
}
