using System.ComponentModel;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Controls;

// Left navigation pane of the main window
// (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.1, D3). One entry is
// the page shown in the window (the medicine list) and stays selected;
// the others open existing windows, never take the selected state and
// carry an "opens a window" glyph. Below a width threshold the pane
// collapses to icons, with the text as tooltip.
internal sealed class NavigationPane : FlowLayoutPanel
{
    // Sizes at 96 DPI and text size Normal; MedReminderFormBase scales
    // the pane's bounds and fonts on load like any other control.
    private const int ItemHeight = 40;
    private const int DefaultExpandedWidth = 240;

    private readonly int _minExpandedWidth;
    private int _expandedWidth;

    private readonly ToolTip _tips = new();
    private bool _collapsed;
    private bool _resizing;

    // Settings passes a wider pane for its longer German captions.
    public NavigationPane(int expandedWidth = DefaultExpandedWidth)
    {
        _minExpandedWidth = expandedWidth;
        _expandedWidth = expandedWidth;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Width = _expandedWidth;
        Padding = new Padding(UiTheme.Space.S, UiTheme.Space.M, UiTheme.Space.S, UiTheme.Space.M);
        Margin = Padding.Empty;
        BackColor = UiTheme.Palette.Background;
        // The divider is drawn at the right edge, which moves when the
        // pane collapses or expands.
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public NavigationItem AddItem(string text, string glyph, bool opensWindow, Action onClick)
    {
        var item = new NavigationItem(text, glyph, opensWindow)
        {
            Height = ItemHeight,
            Width = _expandedWidth - Padding.Horizontal,
            Margin = new Padding(0, 0, 0, UiTheme.Space.XS),
        };
        item.Click += (_, _) => onClick();
        Controls.Add(item);
        return item;
    }

    public void AddSeparator()
    {
        Controls.Add(new Panel
        {
            Height = 1,
            Width = _expandedWidth - Padding.Horizontal,
            Margin = new Padding(UiTheme.Space.S, UiTheme.Space.S, UiTheme.Space.S, UiTheme.Space.S),
            BackColor = UiTheme.Palette.Border,
        });
    }

    // Icons only: the pane shrinks to one square item width and each
    // item shows its text as tooltip.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            ApplyWidth();
        }
    }

    // The user widens the pane by dragging its right edge; it never gets
    // narrower than the width it was built with.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Resizable { get; init; }

    // Width of the expanded pane at 96 DPI and text size Normal, at least
    // the width the pane was built with.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ExpandedWidth
    {
        get => _expandedWidth;
        set
        {
            var width = Math.Max(_minExpandedWidth, value);
            if (width == _expandedWidth) return;
            _expandedWidth = width;
            if (!_collapsed) ApplyWidth();
        }
    }

    private int ScaledItemHeight => Controls.OfType<NavigationItem>().FirstOrDefault()?.Height ?? ItemHeight;

    // Widths follow the scaled item height, so collapsing and expanding
    // after MedReminderFormBase has scaled the pane keep the proportions.
    private void ApplyWidth()
    {
        var itemHeight = ScaledItemHeight;
        var itemWidth = _collapsed
            ? itemHeight
            : (int)Math.Round(itemHeight * (_expandedWidth - Padding.Horizontal) / (float)ItemHeight);
        SuspendLayout();
        foreach (Control child in Controls)
        {
            child.Width = child is NavigationItem ? itemWidth : Math.Max(1, itemWidth - child.Margin.Horizontal);
            if (child is NavigationItem item)
            {
                item.Collapsed = _collapsed;
                _tips.SetToolTip(item, _collapsed ? item.Text : null);
            }
        }
        Width = itemWidth + Padding.Horizontal + (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
        ResumeLayout(performLayout: true);
    }

    // The grip is the right padding, next to the divider: the items never
    // cover it.
    private bool OnGrip(Point location)
        => Resizable && !_collapsed && location.X >= ClientSize.Width - Math.Max(Padding.Right, 4);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && OnGrip(e.Location)) _resizing = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_resizing)
        {
            // The page keeps at least half of the window.
            var maxClientWidth = Parent is { } parent ? parent.ClientSize.Width / 2 : int.MaxValue;
            var clientWidth = Math.Min(e.X + 1, maxClientWidth);
            var scale = ScaledItemHeight / (float)ItemHeight;
            ExpandedWidth = (int)Math.Round((clientWidth - Padding.Horizontal) / scale) + Padding.Horizontal;
        }
        else
        {
            Cursor = OnGrip(e.Location) ? Cursors.SizeWE : Cursors.Default;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _resizing = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        _resizing = false;
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_resizing) Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Divider between the pane and the page.
        using var pen = new Pen(UiTheme.Palette.Border);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}

// One entry of the navigation pane, drawn from the theme palette and
// operable from the keyboard (Tab to reach it, Enter or Space to open).
internal sealed class NavigationItem : Control
{
    private readonly string _glyph;
    private readonly bool _opensWindow;
    private bool _hover;
    private bool _selected;
    private bool _collapsed;

    public NavigationItem(string text, string glyph, bool opensWindow)
    {
        Text = text;
        _glyph = glyph;
        _opensWindow = opensWindow;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }

    // The page shown in the window; entries that open a window are
    // never selected (D3).
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value && !_opensWindow; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Collapsed
    {
        get => _collapsed;
        set { _collapsed = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = UiTheme.Palette;
        var g = e.Graphics;
        var parentBack = Parent?.BackColor ?? p.Background;
        using (var back = new SolidBrush(_selected || _hover ? p.Hover : parentBack))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        // Accent bar on the selected entry, as in Windows 11 navigation.
        if (_selected)
        {
            var barHeight = Height / 2;
            using var accent = new SolidBrush(p.Accent);
            g.FillRectangle(accent, 0, (Height - barHeight) / 2, Math.Max(3, Height / 13), barHeight);
        }

        var iconSize = (int)Math.Round(Height * 0.5);
        var iconLeft = _collapsed ? (Width - iconSize) / 2 : Height / 4;
        var icon = Mdl2Glyph.Create(_glyph, iconSize, p.Text);
        g.DrawImage(icon, iconLeft, (Height - iconSize) / 2, iconSize, iconSize);

        if (!_collapsed)
        {
            var textLeft = iconLeft + iconSize + Height / 4;
            var trailing = 0;
            if (_opensWindow)
            {
                var markSize = (int)Math.Round(Height * 0.35);
                var mark = Mdl2Glyph.Create(Mdl2Glyph.Glyphs.OpenInNewWindow, markSize, p.TextSecondary);
                g.DrawImage(mark, Width - markSize - Height / 4, (Height - markSize) / 2, markSize, markSize);
                trailing = markSize + Height / 2;
            }
            var textBounds = new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft - trailing), Height);
            var font = _selected ? new Font(Font, FontStyle.Bold) : Font;
            try
            {
                TextRenderer.DrawText(g, Text, font, textBounds, p.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            finally
            {
                if (!ReferenceEquals(font, Font)) font.Dispose();
            }
        }

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), p.Text, p.Hover);
        }
    }
}
