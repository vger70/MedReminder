using MedReminder.Application.Abstractions;
using MedReminder.Application.Packages;
using MedReminder.Domain.Stock;

namespace MedReminder.UI.Forms;

// New or edited package of one medicine (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §5.3): quantity, printed expiry, in-use
// period, opening day and batch. A new package entered here has no
// stock movement: it records a box already counted in the stock. The
// closure of an existing package is kept as it is.
internal sealed class PackageEditDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly Guid _medicineId;
    private readonly StockPackage? _existing;
    private readonly DateOnly _today;
    private readonly NumericUpDown _quantity;
    private readonly DateTimePicker _expiry;
    private readonly NumericUpDown _useWithin;
    private readonly DateTimePicker _openedOn;
    private readonly TextBox _batch;
    private readonly Label _error;

    public PackageEditDialog(Guid medicineId, string medicineName, string unit, StockPackage? existing,
        int? defaultUseWithinDays, decimal? defaultQuantity, DateOnly today, ILocalizationService localization)
    {
        _loc = localization;
        _medicineId = medicineId;
        _existing = existing;
        _today = today;

        Text = _loc.Get(existing is null ? "Ui.PackageEditDialog.Title.New" : "Ui.PackageEditDialog.Title.Edit");
        Width = 460;
        Height = 330;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        var header = new Label
        {
            AutoSize = true,
            Text = medicineName,
            Font = new Font(Font, FontStyle.Bold),
        };
        _quantity = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = PackageExpiryRules.MaxQuantity,
            DecimalPlaces = 2,
            Value = Math.Clamp(existing?.Quantity ?? defaultQuantity ?? 1m, 0.01m, PackageExpiryRules.MaxQuantity),
            Width = 100,
        };
        _expiry = PackageInputs.ExpiryPicker(existing?.ExpiresOn, today);
        _useWithin = PackageInputs.UseWithinBox(existing is null ? defaultUseWithinDays : existing.UseWithinDays);
        _openedOn = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            ShowCheckBox = true,
            Checked = existing?.OpenedOn is not null,
            Value = (existing?.OpenedOn ?? today).ToDateTime(TimeOnly.MinValue),
            MaxDate = today.ToDateTime(TimeOnly.MinValue),
            Width = 150,
            Dock = DockStyle.Left,
        };
        _batch = PackageInputs.BatchBox(existing?.Batch);
        _error = DialogLayout.ErrorLabel();

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, string.Empty, header);
        DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Quantity"), DialogLayout.Row(_quantity,
            new Label { AutoSize = true, Text = unit, Padding = new Padding(0, 4, 0, 0) }));
        DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Expiry"), _expiry);
        DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.UseWithin"), DialogLayout.Row(_useWithin,
            new Label { AutoSize = true, Text = _loc.Get("Ui.Packages.Field.UseWithin.Hint"), Padding = new Padding(0, 4, 0, 0) }));
        DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.OpenedOn"), _openedOn);
        DialogLayout.AddRow(table, _loc.Get("Ui.Packages.Field.Batch"), _batch);
        DialogLayout.AddRow(table, string.Empty, _error);

        var okButton = DialogLayout.Button(_loc.Get("Common.Save"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) => Confirm();
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
    }

    public SaveStockPackageCommand? Result { get; private set; }

    private void Confirm()
    {
        var command = new SaveStockPackageCommand(
            _existing?.Id,
            _medicineId,
            _quantity.Value,
            PackageInputs.ReadExpiry(_expiry, _existing?.ExpiresOn),
            PackageInputs.ReadUseWithin(_useWithin),
            _openedOn.Checked ? DateOnly.FromDateTime(_openedOn.Value) : null,
            string.IsNullOrWhiteSpace(_batch.Text) ? null : _batch.Text.Trim(),
            _existing?.ClosedOn,
            _existing?.Closure,
            _existing?.MovementId);
        var candidate = new StockPackage
        {
            MedicineId = _medicineId,
            Quantity = command.Quantity,
            ExpiresOn = command.ExpiresOn,
            UseWithinDays = command.UseWithinDays,
            OpenedOn = command.OpenedOn,
            Batch = command.Batch,
            ClosedOn = command.ClosedOn,
            Closure = command.Closure,
        };
        if (PackageExpiryRules.Validate(candidate, _today) is { } error)
        {
            DialogLayout.ShowError(_error, _loc.Get("Packages.Error." + error));
            return;
        }
        Result = command;
        DialogResult = DialogResult.OK;
    }
}
