using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Dialog template (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.3):
// one form table with the labels on the left, one button bar at the
// bottom right with the primary button last, and field errors shown
// under the field instead of in a message box (F7, F9). Sizes are at
// 96 DPI and text size Normal; MedReminderFormBase scales them on load.
internal static class DialogLayout
{
    public const int ButtonMinWidth = 88;
    public const int ButtonHeight = 32;

    // Two columns: labels sized to the longest caption, fields taking the
    // rest. Rows are added with AddRow.
    public static TableLayoutPanel FormTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.S),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    // Labels align with the top of their field, so a wrapped label or a
    // multi-line field does not centre the label below the first line
    // (baseline L6). An empty label spans the row to the field column.
    public static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Margin = new Padding(UiTheme.Space.XS, UiTheme.Space.S, UiTheme.Space.M, UiTheme.Space.XS),
        };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(caption, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }

    // A dialog button: grows with its caption, at least 88 x 32.
    public static Button Button(string text, DialogResult result = DialogResult.None) => new()
    {
        Text = text,
        DialogResult = result,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(ButtonMinWidth, ButtonHeight),
        Padding = new Padding(UiTheme.Space.M, 0, UiTheme.Space.M, 0),
        Margin = new Padding(UiTheme.Space.S, 0, 0, 0),
    };

    // Bottom button bar, right-aligned: the primary button last (right),
    // Cancel before it, any other buttons to their left. Sets the form's
    // AcceptButton and CancelButton, which the theme fills and outlines.
    public static FlowLayoutPanel ButtonBar(Form form, Button primary, Button? cancel, params Button[] others)
    {
        var bar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.L),
        };
        bar.Controls.Add(primary);
        // A dialog with a single Close button passes it as both.
        if (cancel is not null && !ReferenceEquals(cancel, primary)) bar.Controls.Add(cancel);
        foreach (var other in others) bar.Controls.Add(other);
        form.AcceptButton = primary;
        if (cancel is not null) form.CancelButton = cancel;
        return bar;
    }

    // The dialog grows to its content when the text size or the display
    // scaling makes it larger than the size it was written for; the size
    // set by the dialog stays the minimum. Measured once when the form
    // loads, after MedReminderFormBase has scaled it: Form.AutoSize with
    // a docked, filling table fed the table's width back into the form
    // and kept the dialog from opening.
    public static void GrowToContent(Form form)
        => form.Load += (_, _) => FitClientToContent(form);

    private static void FitClientToContent(Form form)
    {
        var client = form.ClientSize;
        var width = client.Width;
        var height = 0;
        foreach (Control child in form.Controls)
        {
            if (!child.Visible) continue;
            var preferred = child.GetPreferredSize(new Size(client.Width, 0));
            width = Math.Max(width, preferred.Width);
            height += child.Dock is DockStyle.Top or DockStyle.Bottom or DockStyle.Fill
                ? preferred.Height
                : 0;
        }
        var target = new Size(width, Math.Max(client.Height, height));
        if (target == client) return;
        var area = Screen.FromControl(form).WorkingArea;
        var chrome = form.Size - client;
        form.ClientSize = new Size(
            Math.Min(target.Width, area.Width - chrome.Width),
            Math.Min(target.Height, area.Height - chrome.Height));
        if (form.StartPosition == FormStartPosition.CenterParent && form.Owner is { } owner)
        {
            form.Location = new Point(
                owner.Left + (owner.Width - form.Width) / 2,
                owner.Top + (owner.Height - form.Height) / 2);
        }
    }

    // Inline field error (F9): hidden until ShowError sets a message.
    public static Label ErrorLabel() => new()
    {
        AutoSize = true,
        Visible = false,
        ForeColor = UiTheme.Palette.DangerText,
        Margin = new Padding(UiTheme.Space.XS, 0, UiTheme.Space.XS, UiTheme.Space.S),
        MaximumSize = new Size(420, 0),
    };

    // Shows the message under the field and moves the focus to it, or
    // hides the label when the message is null.
    public static void ShowError(Label error, string? message, Control? field = null)
    {
        error.Text = message ?? string.Empty;
        error.Visible = message is not null;
        if (message is not null) field?.Focus();
    }
}
