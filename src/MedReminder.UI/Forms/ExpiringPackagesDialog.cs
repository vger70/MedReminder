using MedReminder.Application.Abstractions;
using MedReminder.Application.Packages;
using MedReminder.Domain.Stock;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Stock → Expiring packages… (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md
// §5.4): the packages of every medicine that are expired or expiring
// soon, expired first. Open packages… shows the packages of the selected
// medicine, where they are opened, finished or discarded; the list then
// reloads. The data and the actions come from the caller.
internal sealed class ExpiringPackagesDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly Func<Task<IReadOnlyList<ExpiringPackageItem>>> _load;
    private readonly Func<ExpiringPackageItem, IWin32Window, Task<bool>> _openPackages;
    private readonly Label _hint;
    private readonly ListView _list;
    private readonly Button _openButton;

    // openPackages shows the packages of the medicine over the given
    // owner and returns whether anything changed.
    public ExpiringPackagesDialog(Func<Task<IReadOnlyList<ExpiringPackageItem>>> load,
        Func<ExpiringPackageItem, IWin32Window, Task<bool>> openPackages, ILocalizationService localization)
    {
        _loc = localization;
        _load = load;
        _openPackages = openPackages;

        Text = _loc.Get("Ui.ExpiringPackagesDialog.Title");
        Width = 860;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _hint = new Label
        {
            Dock = DockStyle.Top,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            Text = _loc.Get("Ui.ExpiringPackagesDialog.Hint"),
        };
        DialogLayout.GrowWithText(_hint);

        _openButton = DialogLayout.Button(_loc.Get("Ui.ExpiringPackagesDialog.Open"));
        _openButton.Enabled = false;
        _openButton.Click += async (_, _) => await OpenAsync();

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.ExpiringPackagesDialog.Column.Medicine"), 240);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.Status"), 130);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.ExpiresOn"), 110);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.InStock"), 130);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.Batch"), 120);
        _list.SelectedIndexChanged += (_, _) => _openButton.Enabled = Selected is not null;
        _list.DoubleClick += async (_, _) => await OpenAsync();

        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton, _openButton);

        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        Shown += async (_, _) => await ReloadAsync();
    }

    private ExpiringPackageItem? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as ExpiringPackageItem : null;

    private async Task ReloadAsync()
    {
        IReadOnlyList<ExpiringPackageItem> items;
        try
        {
            items = await _load();
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.PackagesDialog.Error.Load"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (IsDisposed) return;

        var c = _loc.CurrentCulture;
        _hint.Text = _loc.Get(items.Count == 0 ? "Ui.ExpiringPackagesDialog.Empty" : "Ui.ExpiringPackagesDialog.Hint");
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in items)
        {
            var item = entry.Item;
            var name = entry.MedicineIsActive
                ? entry.MedicineName
                : _loc.Get("Ui.ExpiringPackagesDialog.Inactive", entry.MedicineName);
            var row = new ListViewItem(name) { Tag = entry };
            row.SubItems.Add(_loc.Get("Packages.Status." + item.Status));
            row.SubItems.Add(item.EffectiveExpiry?.ToString("d", c) ?? string.Empty);
            row.SubItems.Add($"{item.Allocated.ToString("0.##", c)} {entry.Unit}".TrimEnd());
            row.SubItems.Add(item.Package.Batch ?? string.Empty);
            // High contrast keeps the theme's colours; the Status column
            // tells the state in words.
            if (!UiColors.HighContrast)
            {
                row.ForeColor = item.Status == PackageExpiryStatus.Expired
                    ? UiTheme.Palette.DangerText
                    : UiTheme.Palette.WarningText;
            }
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        _openButton.Enabled = Selected is not null;
    }

    private async Task OpenAsync()
    {
        if (Selected is not { } entry) return;
        if (await _openPackages(entry, this) && !IsDisposed) await ReloadAsync();
    }
}
