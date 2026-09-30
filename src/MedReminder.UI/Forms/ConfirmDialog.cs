using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Yes/No confirmation (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.3,
// F10). A MessageBox labels its buttons in the language of Windows, not
// in the language chosen in MedReminder; a TaskDialog takes the captions
// from the dictionaries. Same arguments and result as the MessageBox
// call it replaces, so the callers keep comparing with DialogResult.Yes.
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
        var yes = new TaskDialogButton(loc.Get("Common.Yes"));
        var no = new TaskDialogButton(loc.Get("Common.No"));
        var page = new TaskDialogPage
        {
            Caption = caption,
            Text = text,
            Icon = icon switch
            {
                MessageBoxIcon.Warning => TaskDialogIcon.Warning,
                MessageBoxIcon.Error => TaskDialogIcon.Error,
                MessageBoxIcon.Information => TaskDialogIcon.Information,
                _ => TaskDialogIcon.None,
            },
            Buttons = { yes, no },
            DefaultButton = defaultButton == MessageBoxDefaultButton.Button2 ? no : yes,
            // Esc and the close box answer No, as with the MessageBox.
            AllowCancel = true,
            SizeToContent = true,
        };
        var result = owner is null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page);
        return ReferenceEquals(result, yes) ? DialogResult.Yes : DialogResult.No;
    }
}
