using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Equivalent medicines of a package according to the AIFA transparency
// list (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2.6, uses
// U1 to U3): the group, its members cheapest first with public price,
// difference over the reference price, supply state and each package's
// note verbatim; the package of the medicine in bold; the packages of the
// group the profile already has in stock. Factual wording only: no
// ranking, no suggestion to change medicine. The data comes from the
// caller.
internal sealed class EquivalentsDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly Func<Task<EquivalentsView>> _load;
    private readonly Label _header;
    private readonly ListView _list;
    private readonly Label _footer;

    public EquivalentsDialog(string medicineName, Func<Task<EquivalentsView>> load, ILocalizationService localization)
    {
        _loc = localization;
        _load = load;

        Text = _loc.Get("Ui.EquivalentsDialog.Title", medicineName);
        Width = 1080;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _header = new Label
        {
            Dock = DockStyle.Top,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
        };
        DialogLayout.GrowWithText(_header);

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Medicine"), 200);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Package"), 230);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Holder"), 150);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Price"), 90, HorizontalAlignment.Right);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Difference"), 90, HorizontalAlignment.Right);
        _list.Columns.Add(_loc.Get("Ui.MainForm.Column.Supply"), 110);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.AtHome"), 120);
        _list.Columns.Add(_loc.Get("Ui.EquivalentsDialog.Column.Note"), 260);

        _footer = new Label
        {
            Dock = DockStyle.Bottom,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.EquivalentsDialog.Disclaimer"),
        };
        DialogLayout.GrowWithText(_footer);

        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton);

        Controls.Add(_list);
        Controls.Add(_header);
        Controls.Add(_footer);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        Shown += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        EquivalentsView view;
        try
        {
            view = await _load();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.EquivalentsDialog.Error.Load"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (IsDisposed) return;

        _header.Text = HeaderText(view);
        _list.Visible = view.State == EquivalentsState.Listed;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in view.Rows)
        {
            _list.Items.Add(BuildRow(entry));
        }
        _list.EndUpdate();
    }

    private string HeaderText(EquivalentsView view)
    {
        var c = _loc.CurrentCulture;
        switch (view.State)
        {
            case EquivalentsState.NoCode:
                return _loc.Get("Ui.EquivalentsDialog.NoCode");
            case EquivalentsState.NoList:
                return _loc.Get("Ui.EquivalentsDialog.NoList");
            case EquivalentsState.NotListed:
                return _loc.Get("Ui.EquivalentsDialog.NotListed", view.ListDate!.Value.ToString("d", c));
        }

        var group = view.Group!;
        var lines = new List<string>
        {
            _loc.Get("Ui.EquivalentsDialog.Group", group.ActiveIngredient, group.Reference),
            group.ReferencePrice is { } reference
                ? _loc.Get("Ui.EquivalentsDialog.ReferencePrice", Price(reference))
                : _loc.Get("Ui.EquivalentsDialog.ReferencePrice", "—"),
        };
        // U3: the packages of the group already in stock. A note on
        // either package can restrict the substitution: it is shown
        // instead of a plain "equivalent".
        var currentNote = view.Current?.Package.Note;
        foreach (var home in view.AtHome)
        {
            var names = string.Join(", ", home.AtHome);
            var notes = new[] { home.Package.Note, home.IsCurrent ? null : currentNote }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            lines.Add(notes.Count == 0
                ? _loc.Get("Ui.EquivalentsDialog.AtHome", names, home.Package.Name)
                : _loc.Get("Ui.EquivalentsDialog.AtHome.Note", names, home.Package.Name, string.Join(" / ", notes)));
        }
        lines.Add(_loc.Get("Ui.EquivalentsDialog.Source", view.ListDate!.Value.ToString("d", c)));
        return string.Join(Environment.NewLine, lines);
    }

    private ListViewItem BuildRow(EquivalentRow entry)
    {
        var package = entry.Package;
        var row = new ListViewItem(package.Name) { Tag = entry, UseItemStyleForSubItems = true };
        row.SubItems.Add(package.Package);
        row.SubItems.Add(package.Holder);
        row.SubItems.Add(package.PublicPrice is { } price ? Price(price) : "—");
        row.SubItems.Add(package.Difference is { } difference ? Price(difference) : "—");
        row.SubItems.Add(entry.Shortage is { } shortage ? ShortageTexts.Display(shortage, _loc) : string.Empty);
        row.SubItems.Add(entry.IsCurrent
            ? _loc.Get("Ui.EquivalentsDialog.ThisMedicine")
            : string.Join(", ", entry.AtHome));
        row.SubItems.Add(package.Note ?? string.Empty);
        if (package.HasNote) row.ToolTipText = package.Note;
        if (entry.IsCurrent) row.Font = new Font(_list.Font, FontStyle.Bold);
        // High contrast keeps the theme's colours; the Supply column tells
        // the state in words.
        if (entry.Shortage is not null && !UiColors.HighContrast) row.ForeColor = UiTheme.Palette.WarningText;
        return row;
    }

    // Euros in the user's number format.
    private string Price(decimal euros) => string.Format(_loc.CurrentCulture, "{0:0.00} €", euros);
}
