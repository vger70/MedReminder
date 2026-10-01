using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Controls;

// Section list on the left, one section at a time on the right under its
// heading (docs/analysis/ANALYSIS-UI-MODERNIZATION.md §5.2). Replaces a
// TabControl, whose headers stay light in dark mode. Used by the Sync and
// Household dialogs; Settings keeps its own copy of the same layout.
// Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / Ctrl+PageUp move between
// sections, as between tabs.
internal sealed class SectionView : TableLayoutPanel
{
    private readonly NavigationPane _list;
    private readonly Label _heading;
    private readonly Panel _host;
    private readonly List<(NavigationItem Item, string Title, Control Content)> _sections = [];
    private int _selected = -1;

    public SectionView(int listWidth = 220)
    {
        Dock = DockStyle.Fill;
        ColumnCount = 2;
        RowCount = 1;
        Margin = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // A row without a style would grow to its tallest section.
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _list = new NavigationPane(listWidth) { Dock = DockStyle.Left };
        _heading = new Label
        {
            AutoSize = true,
            Font = UiTheme.Fonts.Heading(),
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
            Padding = new Padding(UiTheme.Space.S, 0, 0, 0),
        };
        _host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.Space.M, UiTheme.Space.M, UiTheme.Space.M, 0),
            Margin = Padding.Empty,
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(_heading, 0, 0);
        page.Controls.Add(_host, 0, 1);

        Controls.Add(_list, 0, 0);
        Controls.Add(page, 1, 0);
    }

    // Every section is built up front, as tab pages were, so the text
    // size and the theme reach all of them when the form loads.
    public void AddSection(string title, string glyph, Control content)
    {
        var index = _sections.Count;
        var item = _list.AddItem(title, glyph, opensWindow: false, () => Select(index));
        content.Dock = DockStyle.Fill;
        content.Visible = false;
        _host.Controls.Add(content);
        _sections.Add((item, title, content));
        if (_selected < 0) Select(0);
    }

    public void Select(int index)
    {
        if (index == _selected || index < 0 || index >= _sections.Count) return;
        _selected = index;
        _host.SuspendLayout();
        for (var i = 0; i < _sections.Count; i++)
        {
            var (item, title, content) = _sections[i];
            item.Selected = i == index;
            content.Visible = i == index;
            if (i == index) _heading.Text = title;
        }
        _host.ResumeLayout(performLayout: true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var step = keyData switch
        {
            Keys.Control | Keys.Tab or Keys.Control | Keys.PageDown => 1,
            Keys.Control | Keys.Shift | Keys.Tab or Keys.Control | Keys.PageUp => -1,
            _ => 0,
        };
        if (step != 0 && _sections.Count > 0)
        {
            Select((_selected + step + _sections.Count) % _sections.Count);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
