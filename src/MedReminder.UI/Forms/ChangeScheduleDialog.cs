using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.UI.Controls;

namespace MedReminder.UI.Forms;

// Dialog for a mid-therapy dose / frequency / shape change. The
// ChangeMedicationSchedule use case (Application) creates a new
// entry in MedicationScheduleHistory with EffectiveFrom = the
// effective date — preserving the schedule history (days before
// the effective date keep using the previous dose or shape).
//
// A1: the embedded SchedulePanel lets the user switch the new
// schedule to Weekly, Cyclic, Tapering or Prn. In Simple mode the
// dose / frequency controls above still drive the change and the
// use case rebuilds a FixedDailySchedule internally.
internal sealed class ChangeScheduleDialog : MedReminderFormBase
{
    public ChangeScheduleResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly NumericUpDown _doseBox;
    private readonly NumericUpDown _freqBox;
    private readonly DateTimePicker _effectiveFromPicker;
    private readonly SchedulePanel _schedulePanel;
    private readonly Schedule? _seedSchedule;


    public ChangeScheduleDialog(
        string medicineName,
        decimal currentDose,
        int currentFreq,
        DateOnly minimumEffectiveFrom,
        ILocalizationService localization,
        Schedule? currentSchedule = null)
    {
        _loc = localization;
        Text = _loc.Get("Ui.ChangeScheduleDialog.Title");
        Width = 640;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var header = new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Text = _loc.Get("Ui.ChangeScheduleDialog.Header",
                medicineName, currentDose.ToString("0.##"), currentFreq),
        };

        _doseBox = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = 1000m,
            DecimalPlaces = 2,
            Increment = 0.5m,
            Value = currentDose,
            Dock = DockStyle.Left,
            Width = 120,
        };
        _freqBox = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 24,
            DecimalPlaces = 0,
            Increment = 1,
            Value = currentFreq,
            Dock = DockStyle.Left,
            Width = 120,
        };
        // Default the picker to the therapy's start date rather than
        // to today: when the user changes the dose or shape shortly
        // after creating the medicine, the intent is almost always to
        // make the new schedule effective from the beginning of the
        // therapy, not from the current day. Users who want to backdate
        // (or forward-date) a change to a different day can still edit
        // the picker; MinDate keeps the value from going below the
        // start date. Assigning minDate first also avoids the
        // ArgumentOutOfRangeException DateTimePicker throws when
        // MinDate is set above the current Value.
        var minDate = minimumEffectiveFrom.ToDateTime(TimeOnly.MinValue);
        _effectiveFromPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Dock = DockStyle.Fill,
            MinDate = minDate,
            Value = minDate,
        };

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("Ui.ChangeScheduleDialog.Note"),
        };

        // Dock=Top (not Fill) + AutoSize lets the table grow taller
        // than the surrounding AutoScroll Panel so the scrollbar
        // kicks in when the kind-specific sub-panel expands.
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _schedulePanel = new SchedulePanel(_loc);
        _schedulePanel.ModeChanged += (_, _) => SyncSimpleControlsEnabled();
        // Seeding is deferred to OnLoad: the therapy's current schedule
        // is applied only once the panel's controls have a native
        // handle, otherwise NumericUpDown values assigned to still
        // parent-less controls are not reflected when they are realized
        // (the kind dropdown would populate but the numeric fields
        // would show their defaults).
        _seedSchedule = currentSchedule;

        AddRow(table, string.Empty, header);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.NewDose"), _doseBox);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.NewFrequency"), _freqBox);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.EffectiveFrom"), _effectiveFromPicker);
        AddRow(table, _loc.Get("Ui.Schedule.Mode.Label"), _schedulePanel.Root);
        AddRow(table, string.Empty, note);

        var okButton = new Button { Text = _loc.Get("Ui.ChangeScheduleDialog.Apply"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += OnConfirm;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(table);

        Controls.Add(scroll);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        SyncSimpleControlsEnabled();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Now that the panel's controls are realized, seed it with the
        // therapy's current schedule and sync the Simple / Advanced
        // enabled state to whatever mode the seed selected.
        _schedulePanel.ApplySchedule(_seedSchedule);
        SyncSimpleControlsEnabled();
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var effectiveFrom = DateOnly.FromDateTime(_effectiveFromPicker.Value.Date);
        Schedule? newSchedule = null;
        if (_schedulePanel.AdvancedSelected)
        {
            newSchedule = _schedulePanel.TryBuildSchedule(out var error);
            if (newSchedule is null)
            {
                MessageBox.Show(this,
                    error ?? _loc.Get("Ui.Schedule.Validation.Generic"),
                    _loc.Get("Ui.ChangeScheduleDialog.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }
        Result = new ChangeScheduleResult(_doseBox.Value, (int)_freqBox.Value, effectiveFrom, newSchedule);
    }

    private void SyncSimpleControlsEnabled()
    {
        var simple = !_schedulePanel.AdvancedSelected;
        _doseBox.Enabled = simple;
        _freqBox.Enabled = simple;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }
}

internal sealed record ChangeScheduleResult(
    decimal NewDose,
    int NewFreq,
    DateOnly EffectiveFrom,
    Schedule? NewSchedule = null)
{
    public ChangeMedicationScheduleCommand ToCommand(Guid medicineId)
        => new(medicineId, NewDose, NewFreq, EffectiveFrom, NewSchedule);
}
