namespace MedReminder.UI.UiExtensions;

// Applies the theme to the stock controls a form contains
// (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §4.3, step 1.3): buttons
// become flat, the form's AcceptButton filled with the accent colour
// and the others outlined; grids take the palette's surface, borders
// and selection. Called by MedReminderFormBase when a form loads, and
// again for controls added later (dynamic rows, rebuilt panels).
//
// Sizes are left alone: moving to the 32 px button height and the
// spacing scale is done window by window (step 2), where each layout
// can be checked. Under a Windows high-contrast theme nothing changes:
// the stock rendering already follows the system colours.
internal static class UiThemeApplier
{
    public static void Apply(Control root)
    {
        if (UiTheme.HighContrast) return;
        var accept = root.FindForm()?.AcceptButton as Button;
        ApplyTree(root, UiTheme.Palette, accept);
    }

    private static void ApplyTree(Control control, UiPalette palette, Button? accept)
    {
        switch (control)
        {
            case Button button:
                StyleButton(button, palette, primary: ReferenceEquals(button, accept));
                break;
            case DataGridView grid:
                StyleGrid(grid, palette);
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyTree(child, palette, accept);
        }

        // A handler per container: children added after the form loaded
        // are themed as they arrive, and bring their own handler along.
        control.ControlAdded -= OnControlAdded;
        control.ControlAdded += OnControlAdded;
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is null || UiTheme.HighContrast) return;
        var accept = e.Control.FindForm()?.AcceptButton as Button;
        ApplyTree(e.Control, UiTheme.Palette, accept);
    }

    public static void StyleButton(Button button, UiPalette palette, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        if (primary)
        {
            button.BackColor = palette.Accent;
            button.ForeColor = palette.OnAccent;
            button.FlatAppearance.BorderColor = palette.Accent;
            button.FlatAppearance.MouseOverBackColor = palette.AccentHover;
            button.FlatAppearance.MouseDownBackColor = palette.AccentHover;
        }
        else
        {
            button.BackColor = palette.Surface;
            button.ForeColor = palette.Text;
            button.FlatAppearance.BorderColor = palette.Border;
            button.FlatAppearance.MouseOverBackColor = palette.Hover;
            button.FlatAppearance.MouseDownBackColor = palette.Border;
        }
    }

    // Colours only: row height and column widths belong to each form
    // and to the text-size scaling in MedReminderFormBase.
    public static void StyleGrid(DataGridView grid, UiPalette palette)
    {
        grid.BackgroundColor = palette.Surface;
        grid.GridColor = palette.Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;

        var header = grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = palette.Surface;
        header.ForeColor = palette.TextSecondary;
        header.SelectionBackColor = palette.Surface;
        header.SelectionForeColor = palette.TextSecondary;

        var cells = grid.DefaultCellStyle;
        cells.BackColor = palette.Surface;
        cells.ForeColor = palette.Text;
        cells.SelectionBackColor = palette.Selection;
        cells.SelectionForeColor = palette.SelectionText;
    }
}
