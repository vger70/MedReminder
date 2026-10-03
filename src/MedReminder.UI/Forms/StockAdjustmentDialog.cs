using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Stock;

namespace MedReminder.UI.Forms;

// One dialog for: new package, manual addition, positive or
// negative correction. The caller picks the default
// StockOperationKind; the user can change it before confirming.
// initialQuantity pre-fills the quantity (restock by scan: the size of
// the medicine's last new package); the user can edit it.
internal sealed class StockAdjustmentDialog : MedReminderFormBase
{
    public StockAdjustmentResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly ComboBox _kindBox;
    private readonly NumericUpDown _quantityBox;
    private readonly TextBox _notesBox;
    private readonly PackageDefaults? _packageDefaults;
    private readonly NumericUpDown? _countBox;
    private readonly DateTimePicker? _expiryPicker;
    private readonly NumericUpDown? _useWithinBox;
    private readonly CheckBox? _openedToday;
    private readonly TextBox? _batchBox;

    // packageDefaults: the expiry fields of a new package
    // (ANALYSIS-PACKAGE-EXPIRY.md §5.1); null leaves them out.
    public StockAdjustmentDialog(string medicineName, decimal currentStock, string unit,
        StockOperationKind defaultKind, ILocalizationService localization,
        decimal? initialQuantity = null, PackageDefaults? packageDefaults = null)
    {
        _packageDefaults = packageDefaults;
        _loc = localization;
        Text = _loc.Get("Ui.StockAdjustmentDialog.Title");
        Width = 480;
        Height = 320;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        var currentLabel = new Label
        {
            Text = _loc.Get("Ui.StockAdjustmentDialog.Header",
                medicineName, currentStock.ToString("0.##"), unit),
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
        };

        _kindBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _kindBox.Items.AddRange(
        [
            new KindOption(StockOperationKind.NewPackage, _loc.Get("Ui.StockAdjustmentDialog.Kind.NewPackage")),
            new KindOption(StockOperationKind.ManualAdd, _loc.Get("Ui.StockAdjustmentDialog.Kind.ManualAdd")),
            new KindOption(StockOperationKind.PositiveCorrection, _loc.Get("Ui.StockAdjustmentDialog.Kind.PositiveCorrection")),
            new KindOption(StockOperationKind.NegativeCorrection, _loc.Get("Ui.StockAdjustmentDialog.Kind.NegativeCorrection")),
        ]);
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
        if (initialQuantity is { } quantity)
        {
            _quantityBox.Value = Math.Clamp(quantity, _quantityBox.Minimum, _quantityBox.Maximum);
        }
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
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        DialogLayout.AddRow(table, string.Empty, currentLabel);
        DialogLayout.AddRow(table, _loc.Get("Ui.StockAdjustmentDialog.Field.Kind"), _kindBox);
        DialogLayout.AddRow(table, _loc.Get("Ui.StockAdjustmentDialog.Field.Quantity"), _quantityBox);
        DialogLayout.AddRow(table, _loc.Get("Ui.StockAdjustmentDialog.Field.Notes"), _notesBox);

        // The package fields stay in place and are enabled only for a new
        // package, so the dialog does not change size with the kind.
        if (packageDefaults is { } defaults)
        {
            _countBox = new NumericUpDown { Minimum = 1, Maximum = NewPackagesInput.MaxCount, Value = 1, Width = 80 };
            _expiryPicker = PackageInputs.ExpiryPicker(defaults.ExpiresOn, defaults.Today);
            _useWithinBox = PackageInputs.UseWithinBox(defaults.UseWithinDays);
            _openedToday = new CheckBox { AutoSize = true, Text = _loc.Get("Ui.Packages.Field.OpenedToday") };
            _batchBox = PackageInputs.BatchBox(defaults.Batch);
            DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Count"), _countBox);
            DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Expiry"), _expiryPicker);
            DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.UseWithin"), DialogLayout.Row(_useWithinBox,
                new Label { AutoSize = true, Text = _loc.Get("Ui.Packages.Field.UseWithin.Hint"), Padding = new Padding(0, 4, 0, 0) }));
            DialogLayout.AddRow(table, string.Empty, _openedToday);
            DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Batch"), _batchBox);
            _kindBox.SelectedIndexChanged += (_, _) => UpdatePackageFields();
            UpdatePackageFields();
        }

        var okButton = DialogLayout.Button(_loc.Get("Ui.StockAdjustmentDialog.Apply"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) =>
        {
            var kind = ((KindOption)_kindBox.SelectedItem!).Kind;
            Result = new StockAdjustmentResult(kind, _quantityBox.Value, NullIfBlank(_notesBox.Text),
                kind == StockOperationKind.NewPackage ? ReadPackages() : null);
        };

        var buttonPanel = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttonPanel);
    }


    private void UpdatePackageFields()
    {
        var enabled = ((KindOption)_kindBox.SelectedItem!).Kind == StockOperationKind.NewPackage;
        foreach (Control? control in new Control?[] { _countBox, _expiryPicker, _useWithinBox, _openedToday, _batchBox })
        {
            if (control is not null) control.Enabled = enabled;
        }
    }

    // Null when nothing about the packages was entered: the load stays a
    // plain stock movement, as before the feature.
    private NewPackagesInput? ReadPackages()
    {
        if (_packageDefaults is null) return null;
        var expiresOn = PackageInputs.ReadExpiry(_expiryPicker!, _packageDefaults.ExpiresOn);
        var useWithin = PackageInputs.ReadUseWithin(_useWithinBox!);
        var batch = NullIfBlank(_batchBox!.Text);
        if (expiresOn is null && useWithin is null && batch is null && !_openedToday!.Checked) return null;
        return new NewPackagesInput((int)_countBox!.Value, expiresOn, useWithin, _openedToday!.Checked, batch);
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record KindOption(StockOperationKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}

// Abstracted from the domain StockMovementKind so we do not expose
// in the UI the kinds that are not selectable here (InitialLoad,
// Consumption).
internal enum StockOperationKind
{
    NewPackage,
    ManualAdd,
    PositiveCorrection,
    NegativeCorrection,
}

// What the package fields start from: the in-use period of the
// medicine's latest package, and the expiry and batch of a scan.
internal sealed record PackageDefaults(DateOnly Today, int? UseWithinDays, DateOnly? ExpiresOn, string? Batch);

internal sealed record StockAdjustmentResult(
    StockOperationKind Kind,
    decimal Quantity,
    string? Notes,
    NewPackagesInput? Packages = null)
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
