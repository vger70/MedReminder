using MedReminder.Application.Abstractions;
using MedReminder.Application.Prescriptions;
using MedReminder.Domain.Prescriptions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// New or changed prescription (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2).
// Every date is optional (the picker's check box); "valid until" is
// pre-filled from the issue date with the default validity, and the
// user can change it. Checks the entry with PrescriptionRules and shows
// the error under the form; the caller saves Result. Nothing is logged.
internal sealed class PrescriptionEditDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly ComboBox _medicine;
    private readonly DateTimePicker _requested;
    private readonly DateTimePicker _issued;
    private readonly DateTimePicker _validUntil;
    private readonly DateTimePicker _collected;
    private readonly TextBox _code;
    private readonly NumericUpDown _packages;
    private readonly Label _error;
    private readonly Guid? _id;
    private bool _validUntilTouched;

    public PrescriptionEditDialog(
        IReadOnlyList<(Guid Id, string Name)> medicines,
        Guid? selectedMedicineId,
        Prescription? existing,
        DateOnly today,
        ILocalizationService localization)
    {
        _loc = localization;
        _id = existing?.Id;

        Text = _loc.Get(existing is null ? "Ui.PrescriptionEditDialog.Title.New" : "Ui.PrescriptionEditDialog.Title.Edit");
        Width = 520;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        _medicine = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var (id, name) in medicines) _medicine.Items.Add(new MedicineOption(id, name));
        var wanted = existing?.MedicineId ?? selectedMedicineId;
        _medicine.SelectedItem = _medicine.Items.Cast<MedicineOption>().FirstOrDefault(o => o.Id == wanted);
        if (_medicine.SelectedIndex < 0 && _medicine.Items.Count > 0) _medicine.SelectedIndex = 0;
        // A prescription stays with its medicine.
        _medicine.Enabled = existing is null;

        _requested = DatePicker(existing is null ? today : existing.RequestedOn, today);
        _issued = DatePicker(existing?.IssuedOn, today);
        _validUntil = DatePicker(existing?.ValidUntil, today);
        _collected = DatePicker(existing?.CollectedOn, today);
        _validUntilTouched = existing?.ValidUntil is not null;
        _issued.ValueChanged += (_, _) => PrefillValidUntil();
        _validUntil.ValueChanged += (_, _) => _validUntilTouched = _validUntilTouched || _validUntil.Focused;

        _code = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = PrescriptionRules.MaxCodeLength,
            Text = existing?.Code ?? string.Empty,
        };
        _packages = new NumericUpDown
        {
            Minimum = 0,
            Maximum = PrescriptionRules.MaxPackages,
            Value = existing?.Packages ?? 0,
            Width = 80,
            Dock = DockStyle.Left,
        };

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new Size(440, 0),
            Text = _loc.Get("Ui.PrescriptionEditDialog.Hint", PrescriptionRules.DefaultValidityDays),
        };
        _error = DialogLayout.ErrorLabel();

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Medicine"), _medicine);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Requested"), _requested);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Issued"), _issued);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Code"), _code);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Packages"), _packages);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.ValidUntil"), _validUntil);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Collected"), _collected);
        DialogLayout.AddRow(table, string.Empty, hint);
        DialogLayout.AddRow(table, string.Empty, _error);

        var okButton = DialogLayout.Button(_loc.Get("Common.Save"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) => Confirm();
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
    }

    public SavePrescriptionCommand? Result { get; private set; }

    private static DateTimePicker DatePicker(DateOnly? value, DateOnly today) => new()
    {
        Format = DateTimePickerFormat.Short,
        ShowCheckBox = true,
        Checked = value is not null,
        Value = (value ?? today).ToDateTime(TimeOnly.MinValue),
        Width = 150,
        Dock = DockStyle.Left,
    };

    private static DateOnly? DateOf(DateTimePicker picker)
        => picker.Checked ? DateOnly.FromDateTime(picker.Value.Date) : null;

    // Until the user sets "valid until" by hand, it follows the issue date.
    private void PrefillValidUntil()
    {
        if (_validUntilTouched || !_issued.Checked) return;
        _validUntil.Value = PrescriptionRules.DefaultValidUntil(DateOnly.FromDateTime(_issued.Value.Date))
            .ToDateTime(TimeOnly.MinValue);
        _validUntil.Checked = true;
    }

    private void Confirm()
    {
        if (_medicine.SelectedItem is not MedicineOption medicine)
        {
            DialogLayout.ShowError(_error, _loc.Get("Ui.PrescriptionEditDialog.Error.NoMedicine"), _medicine);
            return;
        }
        var candidate = new Prescription
        {
            MedicineId = medicine.Id,
            RequestedOn = DateOf(_requested),
            IssuedOn = DateOf(_issued),
            Code = string.IsNullOrWhiteSpace(_code.Text) ? null : _code.Text.Trim(),
            Packages = _packages.Value > 0 ? (int)_packages.Value : null,
            ValidUntil = DateOf(_validUntil),
            CollectedOn = DateOf(_collected),
        };
        if (PrescriptionRules.Validate(candidate) is { } error)
        {
            DialogLayout.ShowError(_error, _loc.Get("Ui.PrescriptionEditDialog.Error." + error));
            return;
        }
        Result = new SavePrescriptionCommand(_id, candidate.MedicineId, candidate.RequestedOn, candidate.IssuedOn,
            candidate.Code, candidate.Packages, candidate.ValidUntil, candidate.CollectedOn);
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record MedicineOption(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
