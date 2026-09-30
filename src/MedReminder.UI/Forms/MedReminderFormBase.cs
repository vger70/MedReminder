using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Base class for every form in the app. Single source of truth for
// the icon (shown in title bar + Alt-Tab + task switcher): by
// deriving from here, MainForm and every dialog get the icon without
// having to repeat the assignment in each constructor.
//
// It also applies the per-profile text size and the display scaling
// (docs/notes/EVOLUTION-PROPOSALS.md §3.2) when the form loads, so the
// derived forms keep building their controls at the 96-DPI, Normal
// size they were written for.
internal class MedReminderFormBase : Form
{
    private const float BaselineDpi = 96f;

    private bool _layoutScaled;

    protected MedReminderFormBase()
    {
        // One base font for every window (F1, D2): derived forms no
        // longer set their own, and the text size scales it on load.
        Font = UiTheme.Fonts.Body();

        var icon = AppIcon.Default;
        if (icon is not null)
        {
            Icon = icon;
        }
    }

    // Font factor of the open profile's text size (TextSizes.ScaleOf).
    // Set once in Program after the profile is chosen; windows shown
    // before that (picker, PIN prompt, first-run wizard) stay at 1.
    internal static float TextScale { get; set; } = 1f;

    // Pixel size for an icon drawn at build time: glyph bitmaps are not
    // resized by ScaleLayout, so they are rendered at the size they
    // will be shown, following the display and the text size.
    protected int ScaledIconSize(int pixelsAt96Dpi)
        => (int)Math.Round(pixelsAt96Dpi * Math.Max(1f, TextScale) * Math.Max(1f, DeviceDpi / BaselineDpi));

    protected override void OnLoad(EventArgs e)
    {
        ScaleLayout();
        UiThemeApplier.Apply(this);
        // Form.OnLoad centres modal dialogs, so it runs after the
        // resize, and the Load handlers of the derived forms see the
        // final layout (rows they add use the scaled row template).
        base.OnLoad(e);
    }

    // The forms build their controls in pixels at 96 DPI and never set
    // AutoScaleMode, so WinForms scales neither the bounds (display
    // scaling above 100 %) nor anything but the fonts. Fonts are in
    // points and already follow the display DPI; they grow only by the
    // text size. Bounds, grid rows and list columns grow by both.
    private void ScaleLayout()
    {
        if (_layoutScaled) return;
        _layoutScaled = true;

        var fontFactor = TextScale > 1f ? TextScale : 1f;
        var boundsFactor = fontFactor * Math.Max(1f, DeviceDpi / BaselineDpi);
        if (fontFactor == 1f && boundsFactor == 1f) return;

        SuspendLayout();
        try
        {
            if (fontFactor != 1f)
            {
                ScaleFonts(fontFactor);
            }
            Scale(new SizeF(boundsFactor, boundsFactor));
            ScaleItemSizes(this, boundsFactor);
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }
        FitToWorkingArea();
    }

    // Every font is read before any is changed and then set scaled
    // from its original value, so ambient and explicit fonts (bold
    // titles, monospace codes, grid cell styles) end up the same.
    private void ScaleFonts(float factor)
    {
        var controls = new List<(Control Control, Font Font)>();
        var items = new List<(ToolStripItem Item, Font Font)>();
        var cellStyles = new List<(DataGridViewCellStyle Style, Font Font)>();
        Collect(this, controls, items, cellStyles);

        foreach (var (control, font) in controls) control.Font = Scaled(font, factor);
        foreach (var (item, font) in items) item.Font = Scaled(font, factor);
        foreach (var (style, font) in cellStyles) style.Font = Scaled(font, factor);
    }

    private static void Collect(
        Control control,
        List<(Control, Font)> controls,
        List<(ToolStripItem, Font)> items,
        List<(DataGridViewCellStyle, Font)> cellStyles)
    {
        controls.Add((control, control.Font));
        if (control is ToolStrip strip)
        {
            CollectItems(strip, strip.Items, controls, items);
        }
        if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.HasDefaultCellStyle && column.DefaultCellStyle.Font is { } font)
                {
                    cellStyles.Add((column.DefaultCellStyle, font));
                }
            }
        }
        if (control.ContextMenuStrip is { } menu)
        {
            Collect(menu, controls, items, cellStyles);
        }
        foreach (Control child in control.Controls)
        {
            Collect(child, controls, items, cellStyles);
        }
    }

    // An item whose font differs from its strip's was given one
    // explicitly; the others follow the strip. Drop-down menus are
    // separate windows with their own font.
    private static void CollectItems(
        ToolStrip owner,
        ToolStripItemCollection collection,
        List<(Control, Font)> controls,
        List<(ToolStripItem, Font)> items)
    {
        foreach (ToolStripItem item in collection)
        {
            if (!Equals(item.Font, owner.Font))
            {
                items.Add((item, item.Font));
            }
            if (item is ToolStripDropDownItem { HasDropDownItems: true } dropDownItem)
            {
                var dropDown = dropDownItem.DropDown;
                controls.Add((dropDown, dropDown.Font));
                CollectItems(dropDown, dropDown.Items, controls, items);
            }
        }
    }

    private static Font Scaled(Font font, float factor)
        => new(font.FontFamily, font.Size * factor, font.Style, font.Unit, font.GdiCharSet, font.GdiVerticalFont);

    // Control.Scale moves and resizes controls, including padding,
    // margins and min/max sizes, but leaves alone the sizes that grids
    // and list views keep outside their bounds.
    private static void ScaleItemSizes(Control control, float factor)
    {
        switch (control)
        {
            case DataGridView grid:
                ScaleGrid(grid, factor);
                break;
            case ListView list:
                foreach (ColumnHeader column in list.Columns)
                {
                    column.Width = ScaledPixels(column.Width, factor);
                }
                break;
        }
        foreach (Control child in control.Controls)
        {
            ScaleItemSizes(child, factor);
        }
    }

    private static void ScaleGrid(DataGridView grid, float factor)
    {
        grid.RowTemplate.Height = ScaledPixels(grid.RowTemplate.Height, factor);
        foreach (DataGridViewRow row in grid.Rows)
        {
            row.Height = grid.RowTemplate.Height;
        }
        if (grid.ColumnHeadersHeightSizeMode != DataGridViewColumnHeadersHeightSizeMode.AutoSize)
        {
            grid.ColumnHeadersHeight = ScaledPixels(grid.ColumnHeadersHeight, factor);
        }
        foreach (DataGridViewColumn column in grid.Columns)
        {
            // Read the width first: raising MinimumWidth above it
            // widens the column on its own.
            var width = column.Width;
            column.MinimumWidth = ScaledPixels(column.MinimumWidth, factor);
            if (column.InheritedAutoSizeMode != DataGridViewAutoSizeColumnMode.Fill)
            {
                column.Width = Math.Max(column.MinimumWidth, ScaledPixels(width, factor));
            }
        }
    }

    private static int ScaledPixels(int value, float factor)
        => value <= 0 ? value : (int)Math.Round(value * factor);

    // A dialog built for 96 DPI and grown by 1.5 can exceed a small
    // screen; keep it inside the working area, where AutoScroll or
    // docked layouts take over.
    private void FitToWorkingArea()
    {
        if (WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        var width = Math.Min(Width, area.Width);
        var height = Math.Min(Height, area.Height);
        if (width != Width || height != Height)
        {
            Size = new Size(width, height);
        }
        if (!Modal && StartPosition == FormStartPosition.CenterScreen)
        {
            CenterToScreen();
        }
    }
}
