using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;

namespace MedReminder.UI.Forms;

// Dialog per il cambio dose / frequenza a metà terapia. Il use case
// ChangeMedicationSchedule (Application) crea una nuova entry in
// MedicationScheduleHistory con EffectiveFrom = data di decorrenza —
// preserva la storia della schedule (i giorni prima della decorrenza
// continuano a usare la dose precedente).
internal sealed class ChangeScheduleDialog : MedReminderFormBase
{
    public ChangeScheduleResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly NumericUpDown _doseBox;
    private readonly NumericUpDown _freqBox;
    private readonly DateTimePicker _effectiveFromPicker;

    public ChangeScheduleDialog(
        string medicineName,
        decimal currentDose,
        int currentFreq,
        DateOnly minimumEffectiveFrom,
        ILocalizationService localization)
    {
        _loc = localization;
        Text = _loc.Get("Ui.ChangeScheduleDialog.Title");
        Width = 500;
        Height = 340;
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
        // Se la StartDate della medicina è nel futuro, MinDate finisce
        // sopra Today e assegnare Value = Today solleva ArgumentOutOfRangeException.
        // Il default sensato è il primo giorno ammesso.
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

        AddRow(table, string.Empty, header);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.NewDose"), _doseBox);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.NewFrequency"), _freqBox);
        AddRow(table, _loc.Get("Ui.ChangeScheduleDialog.Field.EffectiveFrom"), _effectiveFromPicker);
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

        Controls.Add(table);
        Controls.Add(buttonPanel);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var effectiveFrom = DateOnly.FromDateTime(_effectiveFromPicker.Value.Date);
        Result = new ChangeScheduleResult(_doseBox.Value, (int)_freqBox.Value, effectiveFrom);
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
    DateOnly EffectiveFrom)
{
    public ChangeMedicationScheduleCommand ToCommand(Guid medicineId)
        => new(medicineId, NewDose, NewFreq, EffectiveFrom);
}
