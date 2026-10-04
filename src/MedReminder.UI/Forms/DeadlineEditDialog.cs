using MedReminder.Application.Abstractions;
using MedReminder.Application.Deadlines;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// New or changed administrative deadline (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.6). The date, the notice and the
// recurrence are entered by the user: validity periods differ by plan
// and region, so nothing is pre-filled from a rule. Checks the entry with
// DeadlineRules and shows the error under the form; the caller saves
// Result. Nothing is logged.
internal sealed class DeadlineEditDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly ComboBox _kind;
    private readonly TextBox _label;
    private readonly ComboBox _medicine;
    private readonly DateTimePicker _dueOn;
    private readonly NumericUpDown _leadDays;
    private readonly CheckBox _repeat;
    private readonly NumericUpDown _repeatMonths;
    private readonly CheckBox _windows;
    private readonly CheckBox _email;
    private readonly DateTimePicker _doneOn;
    private readonly Label _error;
    private readonly Guid? _id;

    public DeadlineEditDialog(
        IReadOnlyList<(Guid Id, string Name)> medicines,
        Guid? selectedMedicineId,
        Deadline? existing,
        DateOnly today,
        ILocalizationService localization)
    {
        _loc = localization;
        _id = existing?.Id;

        Text = _loc.Get(existing is null ? "Ui.DeadlineEditDialog.Title.New" : "Ui.DeadlineEditDialog.Title.Edit");
        Width = 560;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        _kind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var kind in Enum.GetValues<DeadlineKind>())
        {
            _kind.Items.Add(new KindOption(kind, DeadlineTexts.Kind(kind, _loc)));
        }
        _kind.SelectedItem = _kind.Items.Cast<KindOption>()
            .First(o => o.Kind == (existing?.Kind ?? DeadlineKind.TherapeuticPlan));

        _label = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = DeadlineRules.MaxLabelLength,
            Text = existing?.Label ?? string.Empty,
        };

        // A deadline of the profile has no medicine: the first entry.
        _medicine = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _medicine.Items.Add(new MedicineOption(null, _loc.Get("Ui.DeadlineEditDialog.NoMedicine")));
        foreach (var (id, name) in medicines) _medicine.Items.Add(new MedicineOption(id, name));
        var wanted = existing is null ? selectedMedicineId : existing.MedicineId;
        _medicine.SelectedItem = _medicine.Items.Cast<MedicineOption>().FirstOrDefault(o => o.Id == wanted)
            ?? _medicine.Items[0];

        _dueOn = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Value = (existing?.DueOn ?? today).ToDateTime(TimeOnly.MinValue),
            Width = 150,
            Dock = DockStyle.Left,
        };
        _leadDays = new NumericUpDown
        {
            Minimum = 0,
            Maximum = DeadlineRules.MaxLeadDays,
            Value = existing?.LeadDays ?? DeadlineRules.DefaultLeadDays,
            Width = 80,
            Dock = DockStyle.Left,
        };

        _repeat = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.DeadlineEditDialog.Repeat"),
            Checked = existing?.RepeatMonths is not null,
        };
        _repeatMonths = new NumericUpDown
        {
            Minimum = 1,
            Maximum = DeadlineRules.MaxRepeatMonths,
            Value = existing?.RepeatMonths ?? 12,
            Width = 80,
        };
        var repeatRow = DialogLayout.Row(_repeat, _repeatMonths,
            new Label { AutoSize = true, Text = _loc.Get("Ui.DeadlineEditDialog.Months"), Padding = new Padding(0, 4, 0, 0) });

        var channels = existing?.Channels ?? NotificationChannels.Both;
        _windows = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.DeadlineEditDialog.Channel.Windows"),
            Checked = (channels & NotificationChannels.Windows) != 0,
        };
        _email = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.DeadlineEditDialog.Channel.Email"),
            Checked = (channels & NotificationChannels.Email) != 0,
        };
        var channelRow = DialogLayout.Row(_windows, _email);

        _doneOn = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            ShowCheckBox = true,
            Checked = existing?.DoneOn is not null,
            Value = (existing?.DoneOn ?? today).ToDateTime(TimeOnly.MinValue),
            Width = 150,
            Dock = DockStyle.Left,
        };

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new Size(460, 0),
            Text = _loc.Get("Ui.DeadlineEditDialog.Hint"),
        };
        _error = DialogLayout.ErrorLabel();

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.Kind"), _kind);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.Label"), _label);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.Medicine"), _medicine);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.DueOn"), _dueOn);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.LeadDays"), _leadDays);
        DialogLayout.AddRow(table, string.Empty, repeatRow);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.Channels"), channelRow);
        DialogLayout.AddRow(table, _loc.Get("Ui.DeadlineEditDialog.DoneOn"), _doneOn);
        DialogLayout.AddRow(table, string.Empty, hint);
        DialogLayout.AddRow(table, string.Empty, _error);

        // A recurring deadline is never closed: Done moves it on.
        _repeat.CheckedChanged += (_, _) => UpdateRepeat();
        UpdateRepeat();

        var okButton = DialogLayout.Button(_loc.Get("Common.Save"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) => Confirm();
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
    }

    public SaveDeadlineCommand? Result { get; private set; }

    private void UpdateRepeat()
    {
        _repeatMonths.Enabled = _repeat.Checked;
        _doneOn.Enabled = !_repeat.Checked;
        if (_repeat.Checked) _doneOn.Checked = false;
    }

    private void Confirm()
    {
        var channels = NotificationChannels.None;
        if (_windows.Checked) channels |= NotificationChannels.Windows;
        if (_email.Checked) channels |= NotificationChannels.Email;
        var candidate = new Deadline
        {
            MedicineId = (_medicine.SelectedItem as MedicineOption)?.Id,
            Kind = ((KindOption)_kind.SelectedItem!).Kind,
            Label = string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(),
            DueOn = DateOnly.FromDateTime(_dueOn.Value.Date),
            LeadDays = (int)_leadDays.Value,
            RepeatMonths = _repeat.Checked ? (int)_repeatMonths.Value : null,
            Channels = channels,
            DoneOn = _doneOn.Checked && !_repeat.Checked ? DateOnly.FromDateTime(_doneOn.Value.Date) : null,
        };
        if (DeadlineRules.Validate(candidate) is { } error)
        {
            DialogLayout.ShowError(_error, _loc.Get("Ui.DeadlineEditDialog.Error." + error),
                error is DeadlineError.NoLabel or DeadlineError.Label ? _label : null);
            return;
        }
        Result = new SaveDeadlineCommand(_id, candidate.MedicineId, candidate.Kind, candidate.Label, candidate.DueOn,
            candidate.LeadDays, candidate.RepeatMonths, candidate.Channels, candidate.DoneOn);
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record KindOption(DeadlineKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record MedicineOption(Guid? Id, string Name)
    {
        public override string ToString() => Name;
    }
}
