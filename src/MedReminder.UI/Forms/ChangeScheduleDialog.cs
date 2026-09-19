using System.Windows.Forms;
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

    private int _simpleHeight;
    private const int AdvancedHeightBonus = 320;

    public ChangeScheduleDialog(
        string medicineName,
        decimal currentDose,
        int currentFreq,
        DateOnly minimumEffectiveFrom,
        ILocalizationService localization)
    {
        _loc = localization;
        Text = _loc.Get("Ui.ChangeScheduleDialog.Title");
        Width = 640;
        Height = 560;
        _simpleHeight = Height;
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
        // If the medicine's StartDate is in the future, MinDate ends
        // up above Today and assigning Value = Today throws
        // ArgumentOutOfRangeException. The sensible default is the
        // first allowed day.
        var minDate = minimumEffectiveFrom.ToDateTime(TimeOnly.MinValue);
        var initialValue = DateTime.Today >= minDate ? DateTime.Today : minDate;
        _effectiveFromPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Dock = DockStyle.Fill,
            MinDate = minDate,
            Value = initialValue,
        };

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("Ui.ChangeScheduleDialog.Note"),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _schedulePanel = new SchedulePanel(_loc);
        _schedulePanel.ModeChanged += (_, _) =>
        {
            SyncSimpleControlsEnabled();
            AdjustDialogHeightForScheduleMode();
        };

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

    // Grows the dialog when Advanced is selected so the kind-specific
    // sub-panel is not hidden behind the Apply / Cancel buttons;
    // shrinks back to the base Simple layout on Simple. Screen-bound.
    private void AdjustDialogHeightForScheduleMode()
    {
        var target = _schedulePanel.AdvancedSelected
            ? _simpleHeight + AdvancedHeightBonus
            : _simpleHeight;
        var workingArea = Screen.FromControl(this).WorkingArea.Height;
        var cap = (int)(workingArea * 0.9);
        Height = Math.Min(target, cap);
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
