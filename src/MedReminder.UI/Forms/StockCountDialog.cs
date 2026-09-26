using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;

namespace MedReminder.UI.Forms;

// Guided stock count: the user types the quantity found in the
// cabinet and sees, before confirming, the expected stock, the stock
// discrepancy and the effect on the run-out date. All figures come
// from the StockCountSnapshot built by ReconcileStock.LoadAsync; the
// dialog only formats them. The gap is a stock discrepancy, never a
// count of missed or extra doses.
internal sealed class StockCountDialog : MedReminderFormBase
{
    public StockCountResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly StockCountSnapshot _snapshot;
    private readonly string _unit;
    private readonly NumericUpDown _countedBox;
    private readonly NumericUpDown _takenTodayBox;
    private readonly Label _expectedValue;
    private readonly Label _gapValue;
    private readonly Label _runOutValue;
    private readonly Label _infoLabel;
    private readonly TextBox _notesBox;

    public StockCountDialog(string medicineName, string unit, StockCountSnapshot snapshot,
        ILocalizationService localization)
    {
        _loc = localization;
        _snapshot = snapshot;
        _unit = unit;
        Text = _loc.Get("Ui.StockCountDialog.Title");
        Width = 520;
        Height = 440;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var header = new Label
        {
            Text = medicineName,
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
        };

        var initial = snapshot.Evaluate(0m, snapshot.DefaultTakenToday).ExpectedQuantity;
        _countedBox = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 100000m,
            DecimalPlaces = 2,
            Increment = 1m,
            Value = Math.Min(initial, 100000m),
            Dock = DockStyle.Left,
            Width = 120,
        };
        _takenTodayBox = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = snapshot.TodayScheduledQuantity,
            DecimalPlaces = 2,
            Increment = 0.5m,
            Value = snapshot.DefaultTakenToday,
            Dock = DockStyle.Left,
            Width = 120,
            Enabled = snapshot.TodayScheduledQuantity > 0m,
        };
        _expectedValue = new Label { AutoSize = true, Margin = new Padding(4, 8, 4, 4) };
        _gapValue = new Label
        {
            AutoSize = true,
            Margin = new Padding(4, 8, 4, 4),
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
        };
        _runOutValue = new Label { AutoSize = true, Margin = new Padding(4, 8, 4, 4) };
        _infoLabel = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(310, 0), Margin = new Padding(4, 8, 4, 4) };
        _notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 60,
            ScrollBars = ScrollBars.Vertical,
            MaxLength = 500,
            PlaceholderText = _loc.Get("Ui.StockCountDialog.DefaultNote"),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, string.Empty, header);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.Counted"), _countedBox);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.TakenToday",
            snapshot.TodayScheduledQuantity.ToString("0.##"), unit), _takenTodayBox);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.Expected"), _expectedValue);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.Gap"), _gapValue);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.RunOut"), _runOutValue);
        AddRow(table, string.Empty, _infoLabel);
        AddRow(table, _loc.Get("Ui.StockCountDialog.Field.Notes"), _notesBox);

        var okButton = new Button { Text = _loc.Get("Ui.StockCountDialog.Apply"), DialogResult = DialogResult.OK, AutoSize = true, Width = 120, Height = 32 };
        var cancelButton = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += (_, _) =>
        {
            Result = new StockCountResult(
                _countedBox.Value,
                _takenTodayBox.Value,
                string.IsNullOrWhiteSpace(_notesBox.Text) ? null : _notesBox.Text.Trim());
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

        _countedBox.ValueChanged += (_, _) => RefreshPreview();
        _takenTodayBox.ValueChanged += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var preview = _snapshot.Evaluate(_countedBox.Value, _takenTodayBox.Value);
        _expectedValue.Text = $"{preview.ExpectedQuantity:0.##} {_unit}";
        _gapValue.Text = preview.Correction == 0m
            ? _loc.Get("Ui.StockCountDialog.NoGap")
            : $"{preview.Gap.ToString("+0.##;-0.##;0")} {_unit}";

        // Explain what is written when it differs from what the user
        // typed: a ledger alignment below zero, or the start-of-day
        // stock the list keeps showing until today's consumption is
        // recorded.
        var notes = new List<string>(2);
        if (preview.LedgerAlignment != 0m)
        {
            notes.Add(_loc.Get("Ui.StockCountDialog.Note.Alignment",
                preview.Correction.ToString("+0.##;-0.##"), _unit));
        }
        if (preview.StockShownAfter != preview.CountedQuantity)
        {
            notes.Add(_loc.Get("Ui.StockCountDialog.Note.StartOfDay",
                preview.StockShownAfter.ToString("0.##"), _unit));
        }
        _infoLabel.Text = string.Join(Environment.NewLine, notes);
        _runOutValue.Text = _loc.Get("Ui.StockCountDialog.RunOutChange",
            FormatRunOut(preview.ForecastBefore), FormatRunOut(preview.ForecastAfter));
    }

    private string FormatRunOut(RunOutForecastResult forecast) =>
        forecast.EstimatedRunOutDate?.ToString("d") ?? _loc.Get("Ui.StockCountDialog.RunOutNone");

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }
}

internal sealed record StockCountResult(
    decimal CountedQuantity,
    decimal TakenToday,
    string? Notes)
{
    public ReconcileStockCommand ToCommand(Guid medicineId, string defaultNote) =>
        new(medicineId, CountedQuantity, TakenToday, Notes ?? defaultNote);
}
