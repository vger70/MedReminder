using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Prescriptions;
using MedReminder.Domain.Prescriptions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Therapy → Prescriptions… (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2):
// every prescription of the profile, the ones to collect first, with
// New, Edit, Collected today and Delete. For a repeatable prescription,
// "Dispensations" shows collected / allowed and "Collected today" records
// a dispensation. "Regional prescription service" opens the service of
// the profile's region (RegionalServicePanel). The data and the writes
// come from the caller, so the dialog holds no Application logic. The
// prescription code is shown, never logged.
internal sealed class PrescriptionsDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly PrescriptionsDialogActions _actions;
    private readonly ListView _list;
    private readonly Button _editButton;
    private readonly Button _collectButton;
    private readonly Button _deleteButton;

    public PrescriptionsDialog(PrescriptionsDialogActions actions, ILocalizationService localization,
        RegionalServiceActions? regional = null)
    {
        _loc = localization;
        _actions = actions;

        Text = _loc.Get("Ui.PrescriptionsDialog.Title");
        Width = 960;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 48,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            Text = _loc.Get("Ui.PrescriptionsDialog.Hint", PrescriptionRules.ReminderLeadDays),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Medicine"), 200);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Status"), 110);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Requested"), 95);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Issued"), 95);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.ValidUntil"), 95);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Packages"), 70);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Code"), 140);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Collected"), 95);
        _list.Columns.Add(_loc.Get("Ui.PrescriptionsDialog.Column.Dispensations"), 95);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await EditAsync();

        var newButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionsDialog.New"));
        _editButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionsDialog.Edit"));
        _collectButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionsDialog.Collected"));
        _deleteButton = DialogLayout.Button(_loc.Get("Ui.PrescriptionsDialog.Delete"));
        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        newButton.Click += async (_, _) => await NewAsync();
        _editButton.Click += async (_, _) => await EditAsync();
        _collectButton.Click += async (_, _) => await CollectAsync();
        _deleteButton.Click += async (_, _) => await DeleteAsync();
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton,
            _deleteButton, _collectButton, _editButton, newButton);

        Controls.Add(_list);
        Controls.Add(hint);
        // Above the button bar: docked after it in z-order.
        if (regional is not null) Controls.Add(new RegionalServicePanel(regional, _loc));
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        UpdateButtons();
        Shown += async (_, _) => await ReloadAsync();
    }

    // True when something was saved or deleted: the caller reloads.
    public bool Changed { get; private set; }

    private PrescriptionListItem? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as PrescriptionListItem : null;

    private void UpdateButtons()
    {
        var selected = Selected;
        _editButton.Enabled = selected is not null;
        _deleteButton.Enabled = selected is not null;
        // A repeatable prescription takes a dispensation only within its
        // validity; a single one can still be marked collected when expired.
        _collectButton.Enabled = selected is { Status: PrescriptionStatus.ToCollect }
            || selected is { Status: PrescriptionStatus.Expired, Prescription.IsRepeatable: false };
    }

    private async Task ReloadAsync(Guid? select = null)
    {
        IReadOnlyList<PrescriptionListItem> items;
        try
        {
            items = await _actions.Load();
        }
        catch (Exception ex)
        {
            Fail("Ui.PrescriptionsDialog.Error.Load", ex);
            return;
        }
        if (IsDisposed) return;

        var c = _loc.CurrentCulture;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in items)
        {
            var p = item.Prescription;
            var row = new ListViewItem(item.MedicineName) { Tag = item };
            row.SubItems.Add(_loc.Get("Prescriptions.Status." + item.Status));
            row.SubItems.Add(Date(p.RequestedOn, c));
            row.SubItems.Add(Date(p.IssuedOn, c));
            row.SubItems.Add(Date(p.ValidUntil, c));
            row.SubItems.Add(p.Packages?.ToString(c) ?? string.Empty);
            row.SubItems.Add(p.Code ?? string.Empty);
            row.SubItems.Add(p.IsRepeatable ? Date(item.Dispensations.LastOrDefault()?.CollectedOn, c) : Date(p.CollectedOn, c));
            row.SubItems.Add(p.IsRepeatable
                ? string.Format(c, "{0} / {1}", item.Dispensations.Count, p.Dispensations)
                : string.Empty);
            row.ForeColor = item.Status switch
            {
                PrescriptionStatus.Expired => UiTheme.Palette.DangerText,
                PrescriptionStatus.Collected => UiTheme.Palette.TextSecondary,
                _ => row.ForeColor,
            };
            _list.Items.Add(row);
            if (p.Id == select) row.Selected = true;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private static string Date(DateOnly? day, CultureInfo c) => day?.ToString("d", c) ?? string.Empty;

    private async Task NewAsync()
    {
        using var dialog = _actions.CreateEditor(null);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private async Task EditAsync()
    {
        if (Selected is not { } item) return;
        using var dialog = _actions.CreateEditor(item);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private async Task SaveAsync(SavePrescriptionCommand command)
    {
        try
        {
            var id = await _actions.Save(command);
            Changed = true;
            await ReloadAsync(id);
        }
        catch (Exception ex)
        {
            Fail("Ui.PrescriptionsDialog.Error.Save", ex);
        }
    }

    private async Task CollectAsync()
    {
        if (Selected is not { } item) return;
        try
        {
            await _actions.Collect(item);
            Changed = true;
            await ReloadAsync(item.Prescription.Id);
        }
        catch (Exception ex)
        {
            Fail("Ui.PrescriptionsDialog.Error.Save", ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } item) return;
        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get("Ui.PrescriptionsDialog.Confirm.Delete", item.MedicineName),
            _loc.Get("Ui.PrescriptionsDialog.Confirm.Delete.Title"),
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await _actions.Delete(item.Prescription.Id);
            Changed = true;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Fail("Ui.PrescriptionsDialog.Error.Save", ex);
        }
    }

    private void Fail(string titleKey, Exception ex)
        => UiMessageBox.Show(this, ex.Message, _loc.Get(titleKey), MessageBoxButtons.OK, MessageBoxIcon.Error);
}

// What the dialog asks of its caller (MainForm), each in its own scope.
internal sealed record PrescriptionsDialogActions(
    Func<Task<IReadOnlyList<PrescriptionListItem>>> Load,
    Func<PrescriptionListItem?, PrescriptionEditDialog> CreateEditor,
    Func<SavePrescriptionCommand, Task<Guid>> Save,
    Func<PrescriptionListItem, Task> Collect,
    Func<Guid, Task> Delete);
