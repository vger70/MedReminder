using System.Windows.Forms;
using MedReminder.Domain.Stock;

namespace MedReminder.UI.Forms;

// Un solo dialog per: nuova confezione, aggiunta manuale, correzione
// positiva o negativa. Il caller sceglie la StockOperationKind di default;
// l'utente può cambiarla prima di confermare.
internal sealed class StockAdjustmentDialog : MedReminderFormBase
{
    public StockAdjustmentResult? Result { get; private set; }

    private readonly ComboBox _kindBox;
    private readonly NumericUpDown _quantityBox;
    private readonly TextBox _notesBox;

    public StockAdjustmentDialog(string medicineName, decimal currentStock, string unit,
        StockOperationKind defaultKind = StockOperationKind.NewPackage)
    {
        Text = "Movimento di magazzino";
        Width = 480;
        Height = 320;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var currentLabel = new Label
        {
            Text = $"{medicineName} — scorta corrente: {currentStock:0.##} {unit}",
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
        };

        _kindBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _kindBox.Items.AddRange(new object[]
        {
            new KindOption(StockOperationKind.NewPackage, "Nuova confezione"),
            new KindOption(StockOperationKind.ManualAdd, "Aggiunta manuale"),
            new KindOption(StockOperationKind.PositiveCorrection, "Correzione in eccesso"),
            new KindOption(StockOperationKind.NegativeCorrection, "Correzione in difetto"),
        });
        _kindBox.SelectedIndex = _kindBox.Items
            .Cast<KindOption>()
            .Select((o, idx) => (o, idx))
            .First(pair => pair.o.Kind == defaultKind).idx;

        _quantityBox = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = 100000m,
            DecimalPlaces = 2,
            Increment = 1m,
            Value = 1m,
            Dock = DockStyle.Left,
            Width = 120,
        };
        _notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 60,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = 500,
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, string.Empty, currentLabel);
        AddRow(table, "Tipo movimento", _kindBox);
        AddRow(table, "Quantità", _quantityBox);
        AddRow(table, "Note", _notesBox);

        var okButton = new Button { Text = "Applica", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += (_, _) =>
        {
            var kind = ((KindOption)_kindBox.SelectedItem!).Kind;
            Result = new StockAdjustmentResult(kind, _quantityBox.Value, NullIfBlank(_notesBox.Text));
        };

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

    private sealed record KindOption(StockOperationKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}

// Astratto dal dominio StockMovementKind per evitare di esporre nella
// UI kind che non sono selezionabili qui (InitialLoad, Consumption).
internal enum StockOperationKind
{
    NewPackage,
    ManualAdd,
    PositiveCorrection,
    NegativeCorrection,
}

internal sealed record StockAdjustmentResult(
    StockOperationKind Kind,
    decimal Quantity,
    string? Notes)
{
    public bool IsPositive => Kind is StockOperationKind.NewPackage
        or StockOperationKind.ManualAdd
        or StockOperationKind.PositiveCorrection;

    public StockMovementKind ToMovementKind() => Kind switch
    {
        StockOperationKind.NewPackage => StockMovementKind.NewPackage,
        StockOperationKind.ManualAdd => StockMovementKind.ManualAdd,
        StockOperationKind.PositiveCorrection => StockMovementKind.PositiveCorrection,
        StockOperationKind.NegativeCorrection => StockMovementKind.NegativeCorrection,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}
