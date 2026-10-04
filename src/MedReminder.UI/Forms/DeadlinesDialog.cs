using MedReminder.Application.Abstractions;
using MedReminder.Application.Deadlines;
using MedReminder.Domain.Deadlines;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Therapy → Deadlines… (docs/notes/EVOLUTION-PROPOSALS-2.md §3.6): every
// administrative deadline of the profile, the overdue and close ones
// first, with New, Edit, Done and Delete. The data and the writes come
// from the caller, so the dialog holds no Application logic. The label
// is shown, never logged.
internal sealed class DeadlinesDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly DeadlinesDialogActions _actions;
    private readonly ListView _list;
    private readonly Button _editButton;
    private readonly Button _doneButton;
    private readonly Button _deleteButton;

    public DeadlinesDialog(DeadlinesDialogActions actions, ILocalizationService localization)
    {
        _loc = localization;
        _actions = actions;

        Text = _loc.Get("Ui.DeadlinesDialog.Title");
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
            Text = _loc.Get("Ui.DeadlinesDialog.Hint"),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.Deadline"), 240);
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.Medicine"), 180);
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.Status"), 110);
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.DueOn"), 95);
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.Repeat"), 120);
        _list.Columns.Add(_loc.Get("Ui.DeadlinesDialog.Column.Done"), 95);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += async (_, _) => await EditAsync();

        var newButton = DialogLayout.Button(_loc.Get("Ui.DeadlinesDialog.New"));
        _editButton = DialogLayout.Button(_loc.Get("Ui.DeadlinesDialog.Edit"));
        _doneButton = DialogLayout.Button(_loc.Get("Ui.DeadlinesDialog.Done"));
        _deleteButton = DialogLayout.Button(_loc.Get("Ui.DeadlinesDialog.Delete"));
        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        newButton.Click += async (_, _) => await NewAsync();
        _editButton.Click += async (_, _) => await EditAsync();
        _doneButton.Click += async (_, _) => await CompleteAsync();
        _deleteButton.Click += async (_, _) => await DeleteAsync();
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton,
            _deleteButton, _doneButton, _editButton, newButton);

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        UpdateButtons();
        Shown += async (_, _) => await ReloadAsync();
    }

    private DeadlineListItem? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as DeadlineListItem : null;

    private void UpdateButtons()
    {
        var selected = Selected;
        _editButton.Enabled = selected is not null;
        _deleteButton.Enabled = selected is not null;
        _doneButton.Enabled = selected is not null && selected.Status != DeadlineStatus.Done;
    }

    private async Task ReloadAsync(Guid? select = null)
    {
        IReadOnlyList<DeadlineListItem> items;
        try
        {
            items = await _actions.Load();
        }
        catch (Exception ex)
        {
            Fail("Ui.DeadlinesDialog.Error.Load", ex);
            return;
        }
        if (IsDisposed) return;

        var c = _loc.CurrentCulture;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in items)
        {
            var d = item.Deadline;
            var row = new ListViewItem(DeadlineTexts.Subject(d, null, _loc)) { Tag = item };
            row.SubItems.Add(item.MedicineName ?? string.Empty);
            row.SubItems.Add(_loc.Get("Deadlines.Status." + item.Status));
            row.SubItems.Add(d.DueOn.ToString("d", c));
            row.SubItems.Add(d.RepeatMonths is { } months
                ? _loc.Get("Ui.DeadlinesDialog.RepeatEvery", months)
                : string.Empty);
            row.SubItems.Add(d.DoneOn?.ToString("d", c) ?? string.Empty);
            row.ForeColor = item.Status switch
            {
                DeadlineStatus.Overdue => UiTheme.Palette.DangerText,
                DeadlineStatus.Done => UiTheme.Palette.TextSecondary,
                _ => row.ForeColor,
            };
            _list.Items.Add(row);
            if (d.Id == select) row.Selected = true;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private async Task NewAsync()
    {
        using var dialog = _actions.CreateEditor(null);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private async Task EditAsync()
    {
        if (Selected is not { } item) return;
        using var dialog = _actions.CreateEditor(item.Deadline);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        await SaveAsync(dialog.Result);
    }

    private async Task SaveAsync(SaveDeadlineCommand command)
    {
        try
        {
            var id = await _actions.Save(command);
            await ReloadAsync(id);
        }
        catch (Exception ex)
        {
            Fail("Ui.DeadlinesDialog.Error.Save", ex);
        }
    }

    private async Task CompleteAsync()
    {
        if (Selected is not { } item) return;
        try
        {
            await _actions.Complete(item.Deadline.Id);
            await ReloadAsync(item.Deadline.Id);
        }
        catch (Exception ex)
        {
            Fail("Ui.DeadlinesDialog.Error.Save", ex);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } item) return;
        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get("Ui.DeadlinesDialog.Confirm.Delete", DeadlineTexts.Subject(item.Deadline, item.MedicineName, _loc)),
            _loc.Get("Ui.DeadlinesDialog.Confirm.Delete.Title"),
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        try
        {
            await _actions.Delete(item.Deadline.Id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Fail("Ui.DeadlinesDialog.Error.Save", ex);
        }
    }

    private void Fail(string titleKey, Exception ex)
        => UiMessageBox.Show(this, ex.Message, _loc.Get(titleKey), MessageBoxButtons.OK, MessageBoxIcon.Error);
}

// What the dialog asks of its caller (MainForm), each in its own scope.
internal sealed record DeadlinesDialogActions(
    Func<Task<IReadOnlyList<DeadlineListItem>>> Load,
    Func<Deadline?, DeadlineEditDialog> CreateEditor,
    Func<SaveDeadlineCommand, Task<Guid>> Save,
    Func<Guid, Task> Complete,
    Func<Guid, Task> Delete);
