using System.Windows.Forms;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;

namespace MedReminder.UI.Forms;

// Dialog per registrare una singola assunzione (spec §6, esposto in
// Incremento 9c). Lo status decide se creare anche uno StockMovement
// Consumption:
//   Taken           → sì, scorta scende di Quantity
//   Skipped         → no, ma la giornata è marcata come "gestita"
//   Cancelled       → no, cancellazione di una assunzione registrata
//                     precedentemente (uso storico/audit).
internal sealed class IntakeDialog : Form
{
    public IntakeResult? Result { get; private set; }

    private readonly ComboBox _statusBox;
    private readonly NumericUpDown _quantityBox;
    private readonly DateTimePicker _dayPicker;
    private readonly TextBox _notesBox;

    public IntakeDialog(string medicineName, string unit, decimal suggestedQuantity)
    {
        Text = "Registra assunzione";
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
            Text = $"{medicineName} — dose suggerita: {suggestedQuantity:0.##} {unit}",
        };

        _statusBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _statusBox.Items.AddRange(new object[]
        {
            new StatusOption(IntakeStatus.Taken, "Assunta"),
            new StatusOption(IntakeStatus.Skipped, "Saltata"),
            new StatusOption(IntakeStatus.Cancelled, "Annullata"),
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
            MaxDate = DateTime.Today,   // non registrabile per il futuro
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
            Text = "Assunta: la scorta scende di Quantità.\n" +
                   "Saltata: nessuna variazione di scorta; il consumo automatico salta questa giornata.\n" +
                   "Annullata: audit trail, nessuna variazione di scorta.",
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
        AddRow(table, "Stato", _statusBox);
        AddRow(table, "Quantità", _quantityBox);
        AddRow(table, "Giorno", _dayPicker);
        AddRow(table, "Note", _notesBox);
        AddRow(table, string.Empty, note);

        var okButton = new Button { Text = "Registra", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
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
