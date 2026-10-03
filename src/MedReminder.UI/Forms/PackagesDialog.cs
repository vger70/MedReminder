using MedReminder.Application.Abstractions;
using MedReminder.Application.Packages;
using MedReminder.Domain.Stock;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Stock → Packages… (docs/analysis/ANALYSIS-PACKAGE-EXPIRY.md §5.3): the
// packages of one medicine, the ones to act on first, with New, Edit,
// Opened today, Finished, Discard and Delete. Used-up and closed
// packages are shown on request. The data and the writes come from the
// caller, so the dialog holds no Application logic. The batch is shown,
// never logged.
internal sealed class PackagesDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly PackagesDialogActions _actions;
    private readonly string _unit;
    private readonly DateOnly _today;
    private readonly ListView _list;
    private readonly CheckBox _showAll;
    private readonly Button _editButton;
    private readonly Button _openedButton;
    private readonly Button _finishedButton;
    private readonly Button _discardButton;
    private readonly Button _deleteButton;

    public PackagesDialog(string medicineName, string unit, DateOnly today, PackagesDialogActions actions,
        ILocalizationService localization)
    {
        _loc = localization;
        _actions = actions;
        _unit = unit;
        _today = today;

        Text = _loc.Get("Ui.PackagesDialog.Title", medicineName);
        Width = 960;
        Height = 480;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            Text = _loc.Get("Ui.PackagesDialog.Hint"),
        };
        DialogLayout.GrowWithText(hint);

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.Status"), 120);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.Expiry"), 90);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.OpenedOn"), 95);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.UseWithin"), 90);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.ExpiresOn"), 110);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.InStock"), 110);
        _list.Columns.Add(_loc.Get("Ui.PackagesDialog.Column.Batch"), 120);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await EditAsync();

        _showAll = new CheckBox
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, 0, 0),
            Text = _loc.Get("Ui.PackagesDialog.ShowAll"),
        };
        _showAll.CheckedChanged += async (_, _) => await ReloadAsync();

        var newButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.New"));
        _editButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.Edit"));
        _openedButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.OpenedToday"));
        _finishedButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.Finished"));
        _discardButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.Discard"));
        _deleteButton = DialogLayout.Button(_loc.Get("Ui.PackagesDialog.Delete"));
        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        newButton.Click += async (_, _) => await NewAsync();
        _editButton.Click += async (_, _) => await EditAsync();
        _openedButton.Click += async (_, _) => await MarkOpenedAsync();
        _finishedButton.Click += async (_, _) => await MarkFinishedAsync();
        _discardButton.Click += async (_, _) => await DiscardAsync();
        _deleteButton.Click += async (_, _) => await DeleteAsync();
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton,
            _deleteButton, _discardButton, _finishedButton, _openedButton, _editButton, newButton);

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(_showAll);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        UpdateButtons();
        Shown += async (_, _) => await ReloadAsync();
    }

    // True when something was saved or deleted: the caller reloads.
    public bool Changed { get; private set; }

    private PackageListItem? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as PackageListItem : null;

    private void UpdateButtons()
    {
        var selected = Selected;
        var open = selected is { Package.IsClosed: false };
        _editButton.Enabled = selected is not null;
        _deleteButton.Enabled = selected is not null;
        _openedButton.Enabled = open && selected!.Package.OpenedOn is null;
        _finishedButton.Enabled = open;
        _discardButton.Enabled = open;
    }

    private async Task ReloadAsync(Guid? select = null)
    {
        IReadOnlyList<PackageListItem> items;
        try
        {
            items = await _actions.Load();
        }
        catch (Exception ex)
        {
            Fail("Ui.PackagesDialog.Error.Load", ex);
            return;
        }
        if (IsDisposed) return;

        var c = _loc.CurrentCulture;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in items)
        {
            if (!_showAll.Checked && item.Status is PackageExpiryStatus.UsedUp or PackageExpiryStatus.Closed) continue;
            var p = item.Package;
            var row = new ListViewItem(StatusText(item)) { Tag = item };
            row.SubItems.Add(p.ExpiresOn?.ToString("MM/yyyy", c) ?? string.Empty);
            row.SubItems.Add(p.OpenedOn?.ToString("d", c) ?? string.Empty);
            row.SubItems.Add(p.UseWithinDays is { } days ? _loc.Get("Ui.PackagesDialog.Days", days) : string.Empty);
            row.SubItems.Add(item.EffectiveExpiry?.ToString("d", c) ?? string.Empty);
            row.SubItems.Add(p.IsClosed ? string.Empty : $"{item.Allocated.ToString("0.##", c)} / {p.Quantity.ToString("0.##", c)} {_unit}".TrimEnd());
            row.SubItems.Add(p.Batch ?? string.Empty);
            // High contrast keeps the theme's colours; the Status column
            // tells the state in words.
            if (!UiColors.HighContrast)
            {
                row.ForeColor = item.Status switch
                {
                    PackageExpiryStatus.Expired => UiTheme.Palette.DangerText,
                    PackageExpiryStatus.ExpiringSoon => UiTheme.Palette.WarningText,
                    PackageExpiryStatus.UsedUp or PackageExpiryStatus.Closed => UiTheme.Palette.TextSecondary,
                    _ => row.ForeColor,
                };
            }
            _list.Items.Add(row);
            if (p.Id == select) row.Selected = true;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private string StatusText(PackageListItem item)
        => item.Package.Closure is { } closure
            ? _loc.Get("Packages.Closure." + closure)
            : _loc.Get("Packages.Status." + item.Status);

    private async Task NewAsync()
    {
        using var dialog = _actions.CreateEditor(null);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private async Task EditAsync()
    {
        if (Selected is not { } item) return;
        using var dialog = _actions.CreateEditor(item.Package);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private Task MarkOpenedAsync()
        => Selected is { } item ? SaveAsync(Command(item.Package) with { OpenedOn = _today }) : Task.CompletedTask;

    private Task MarkFinishedAsync()
        => Selected is { } item
            ? SaveAsync(Command(item.Package) with { ClosedOn = _today, Closure = PackageClosure.Finished })
            : Task.CompletedTask;

    private static SaveStockPackageCommand Command(StockPackage p)
        => new(p.Id, p.MedicineId, p.Quantity, p.ExpiresOn, p.UseWithinDays, p.OpenedOn, p.Batch,
            p.ClosedOn, p.Closure, p.MovementId);

    private async Task SaveAsync(SaveStockPackageCommand command)
    {
        try
        {
            var id = await _actions.Save(command);
            Changed = true;
            await ReloadAsync(id);
        }
        catch (Exception ex)
        {
            Fail("Ui.PackagesDialog.Error.Save", ex);
        }
    }

    private async Task DiscardAsync()
    {
        if (Selected is not { } item) return;
        decimal quantityLeft;
        using (var dialog = new DiscardPackageDialog(item.Allocated, _unit, _loc))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            quantityLeft = dialog.QuantityLeft;
        }
        try
        {
            await _actions.Discard(item.Package.Id, quantityLeft);
            Changed = true;
            await ReloadAsync(item.Package.Id);
        }
        catch (Exception ex)
        {
            Fail("Ui.PackagesDialog.Error.Save", ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } item) return;
        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get("Ui.PackagesDialog.Confirm.Delete"),
            _loc.Get("Ui.PackagesDialog.Confirm.Delete.Title"),
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await _actions.Delete(item.Package.Id);
            Changed = true;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Fail("Ui.PackagesDialog.Error.Save", ex);
        }
    }

    private void Fail(string titleKey, Exception ex)
        => UiMessageBox.Show(this, ex.Message, _loc.Get(titleKey), MessageBoxButtons.OK, MessageBoxIcon.Error);
}

// Discarding a package (§3.5): how much of it is thrown away, pre-filled
// with what the allocation says is still in it. That quantity leaves the
// stock.
internal sealed class DiscardPackageDialog : MedReminderFormBase
{
    private readonly NumericUpDown _quantity;

    public DiscardPackageDialog(decimal allocated, string unit, ILocalizationService localization)
    {
        Text = localization.Get("Ui.DiscardPackageDialog.Title");
        Width = 440;
        Height = 220;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        _quantity = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = PackageExpiryRules.MaxQuantity,
            DecimalPlaces = 2,
            Value = Math.Clamp(allocated, 0m, PackageExpiryRules.MaxQuantity),
            Width = 100,
        };
        var hint = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new Size(380, 0),
            Text = localization.Get("Ui.DiscardPackageDialog.Hint"),
        };
        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, localization.Get("Ui.DiscardPackageDialog.QuantityLeft"), DialogLayout.Row(_quantity,
            new Label { AutoSize = true, Text = unit, Padding = new Padding(0, 4, 0, 0) }));
        DialogLayout.AddRow(table, string.Empty, hint);

        var okButton = DialogLayout.Button(localization.Get("Ui.DiscardPackageDialog.Confirm"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(localization.Get("Common.Cancel"), DialogResult.Cancel);
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);
        Controls.Add(table);
        Controls.Add(buttons);
    }

    public decimal QuantityLeft => _quantity.Value;
}

// What the dialog asks of its caller (MainForm), each in its own scope.
internal sealed record PackagesDialogActions(
    Func<Task<IReadOnlyList<PackageListItem>>> Load,
    Func<StockPackage?, PackageEditDialog> CreateEditor,
    Func<SaveStockPackageCommand, Task<Guid>> Save,
    Func<Guid, decimal, Task> Discard,
    Func<Guid, Task> Delete);
