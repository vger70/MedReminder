using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Yes/No confirmation (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.3,
// F10): a UiMessageBox with Yes and No from the dictionaries, themed like
// the rest of the app. Same arguments and result as the MessageBox call
// it replaced, so the callers keep comparing with DialogResult.Yes.
internal static class ConfirmDialog
{
    public static DialogResult Show(
        ILocalizationService loc,
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        UiMessageBox.Localization ??= loc;
        return UiMessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo, icon, defaultButton);
    }
}
