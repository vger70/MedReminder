using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;

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
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

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
            ForeColor = System.Drawing.Color.DarkGray,
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
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, string.Empty, header);
        AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Status"), _statusBox);
        AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Amount"), _quantityBox);
        AddRow(table, _loc.Get("Ui.IntakeDialog.Field.When"), _dayPicker);
        AddRow(table, _loc.Get("Ui.IntakeDialog.Field.Notes"), _notesBox);
        AddRow(table, string.Empty, note);

        var okButton = new Button { Text = _loc.Get("Ui.IntakeDialog.Save"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
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
        var status = ((StatusOption)_statusBox.SelectedItem!).Status;
        var day = DateOnly.FromDateTime(_dayPicker.Value.Date);
        Result = new IntakeResult(status, _quantityBox.Value, day,
            NullIfBlank(_notesBox.Text));
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
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
