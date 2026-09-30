namespace MedReminder.UI.UiExtensions;

// Menus, toolbars, status bars and context menus drawn from the theme
// palette (docs/analysis/ANALYSIS-UI-MODERNIZATION.md F5, §4.3): flat
// surfaces, a neutral hover, no gradients. Installed once at boot as
// ToolStripManager.Renderer, so every strip left in the default render
// mode, drop-downs and the tray menu included, uses it. Not installed
// under a Windows high-contrast theme, where the default renderer
// already follows the system colours.
internal sealed class UiToolStripRenderer : ToolStripProfessionalRenderer
{
    private readonly UiPalette _palette;

    public UiToolStripRenderer(UiPalette palette)
        : base(new UiColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = false;
    }

    // Items that keep the default system colour take the palette text;
    // an item given its own colour (e.g. the admin badge in the status
    // bar) keeps it.
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.ForeColor.IsSystemColor)
        {
            e.TextColor = e.Item.Enabled ? _palette.Text : _palette.TextSecondary;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? _palette.TextSecondary : _palette.Text;
        base.OnRenderArrow(e);
    }

    private sealed class UiColorTable : ProfessionalColorTable
    {
        private readonly UiPalette _p;

        public UiColorTable(UiPalette palette)
        {
            _p = palette;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _p.Surface;
        public override Color ImageMarginGradientBegin => _p.Surface;
        public override Color ImageMarginGradientMiddle => _p.Surface;
        public override Color ImageMarginGradientEnd => _p.Surface;
        public override Color MenuBorder => _p.Border;

        public override Color MenuItemBorder => _p.Hover;
        public override Color MenuItemSelected => _p.Hover;
        public override Color MenuItemSelectedGradientBegin => _p.Hover;
        public override Color MenuItemSelectedGradientEnd => _p.Hover;
        public override Color MenuItemPressedGradientBegin => _p.Surface;
        public override Color MenuItemPressedGradientMiddle => _p.Surface;
        public override Color MenuItemPressedGradientEnd => _p.Surface;

        public override Color MenuStripGradientBegin => _p.Background;
        public override Color MenuStripGradientEnd => _p.Background;
        public override Color StatusStripGradientBegin => _p.Background;
        public override Color StatusStripGradientEnd => _p.Background;
        public override Color ToolStripGradientBegin => _p.Background;
        public override Color ToolStripGradientMiddle => _p.Background;
        public override Color ToolStripGradientEnd => _p.Background;
        public override Color ToolStripBorder => _p.Background;
        public override Color ToolStripContentPanelGradientBegin => _p.Background;
        public override Color ToolStripContentPanelGradientEnd => _p.Background;
        public override Color ToolStripPanelGradientBegin => _p.Background;
        public override Color ToolStripPanelGradientEnd => _p.Background;
        public override Color OverflowButtonGradientBegin => _p.Background;
        public override Color OverflowButtonGradientMiddle => _p.Background;
        public override Color OverflowButtonGradientEnd => _p.Background;

        public override Color ButtonSelectedHighlight => _p.Hover;
        public override Color ButtonSelectedHighlightBorder => _p.Border;
        public override Color ButtonSelectedBorder => _p.Border;
        public override Color ButtonSelectedGradientBegin => _p.Hover;
        public override Color ButtonSelectedGradientMiddle => _p.Hover;
        public override Color ButtonSelectedGradientEnd => _p.Hover;
        public override Color ButtonPressedHighlight => _p.Border;
        public override Color ButtonPressedHighlightBorder => _p.Border;
        public override Color ButtonPressedBorder => _p.Border;
        public override Color ButtonPressedGradientBegin => _p.Border;
        public override Color ButtonPressedGradientMiddle => _p.Border;
        public override Color ButtonPressedGradientEnd => _p.Border;
        public override Color ButtonCheckedHighlight => _p.Selection;
        public override Color ButtonCheckedHighlightBorder => _p.Border;
        public override Color ButtonCheckedGradientBegin => _p.Selection;
        public override Color ButtonCheckedGradientMiddle => _p.Selection;
        public override Color ButtonCheckedGradientEnd => _p.Selection;

        public override Color CheckBackground => _p.Selection;
        public override Color CheckSelectedBackground => _p.Selection;
        public override Color CheckPressedBackground => _p.Selection;

        public override Color SeparatorDark => _p.Border;
        public override Color SeparatorLight => _p.Background;
        public override Color GripDark => _p.Border;
        public override Color GripLight => _p.Background;
    }
}
