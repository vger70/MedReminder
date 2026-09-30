using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Dialog to record a single intake (spec §6, exposed in
// Increment 9c). The status decides whether to also create a
// Consumption StockMovement:
//   Taken           → yes, stock decreases by Quantity
//   Skipped         → no, but the day is marked as "handled"
//   Cancelled       → no, cancels a previously recorded intake
//                     (historical / audit use).
internal sealed class IntakeDialog : MedReminderFormBase
{
    public IntakeResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly ComboBox _statusBox;
    private readonly NumericUpDown _quantityBox;
    private readonly DateTimePicker _dayPicker;
    private readonly TextBox _notesBox;

    public IntakeDialog(string medicineName, string unit, decimal suggestedQuantity, ILocalizationService localization)
    {
        _loc = localization;
        Text = _loc.Get("Ui.IntakeDialog.Title");
        Width = 480;
        Height = 380;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        var header = new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Text = _loc.Get("Ui.IntakeDialog.Header",
                medicineName, suggestedQuantity.ToString("0.##"), unit),
        };

        _statusBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _statusBox.Items.AddRange(new object[]
        {
            new StatusOption(IntakeStatus.Taken, _loc.Get("Ui.IntakeDialog.Status.Taken")),
            new StatusOption(IntakeStatus.Skipped, _loc.Get("Ui.IntakeDialog.Status.Skipped")),
            new StatusOption(IntakeStatus.Cancelled, _loc.Get("Ui.IntakeDialog.Status.Cancelled")),
        });
        _statusBox.SelectedIndex = 0;

        _quantityBox = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = 1000m,
            DecimalPlaces = 2,
            Increment = 0.5m,
            Value = suggestedQuantity > 0m ? suggestedQuantity : 1m,
            Dock = DockStyle.Left,
            Width = 120,
        };

        _dayPicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Dock = DockStyle.Fill,
            Value = DateTime.Today,
            MaxDate = DateTime.Today,   // not recordable for the future
        };

        _notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 60,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = 500,
        };

        var note = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new System.Drawing.Size(420, 0),
            Text = _loc.Get("Ui.IntakeDialog.Note"),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        DialogLayout.AddRow(table, string.Empty, header);
        DialogLayout.AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Status"), _statusBox);
        DialogLayout.AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Amount"), _quantityBox);
        DialogLayout.AddRow(table, _loc.Get("Ui.IntakeDialog.Field.When"), _dayPicker);
        DialogLayout.AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Notes"), _notesBox);
        DialogLayout.AddRow(table, string.Empty, note);

        var okButton = DialogLayout.Button(_loc.Get("Ui.IntakeDialog.Save"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += OnConfirm;

        var buttonPanel = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttonPanel);
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var status = ((StatusOption)_statusBox.SelectedItem!).Status;
        var day = DateOnly.FromDateTime(_dayPicker.Value.Date);
        Result = new IntakeResult(status, _quantityBox.Value, day,
            NullIfBlank(_notesBox.Text));
    }


    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record StatusOption(IntakeStatus Status, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed record IntakeResult(
    IntakeStatus Status,
    decimal Quantity,
    DateOnly Day,
    string? Notes)
{
    public RegisterIntakeCommand ToCommand(Guid medicineId)
        => new(medicineId, Day, Status, Quantity, Notes);
}
