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
//
// "Repeatable prescription" enables the number of dispensations and the
// list of dispensations collected (Add, Edit, Remove) and disables the
// single "collected on" date; ticking it pre-fills "valid until" with
// the repeatable default unless the user set it. The fields stay in place
// when disabled, so the dialog keeps its size.
//
// "Paste NRE" (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md
// §3.3), offered with Italy as reference country: reads the clipboard
// only when clicked, writes the normalised prescription number when the
// text is one (NreCode), and fills an empty issue date with today.
internal sealed class PrescriptionEditDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly ComboBox _medicine;
    private readonly DateTimePicker _requested;
    private readonly DateTimePicker _issued;
    private readonly DateTimePicker _validUntil;
    private readonly DateTimePicker _collected;
    private readonly TextBox _code;
    private readonly Label _codeError;
    private readonly NumericUpDown _packages;
    private readonly CheckBox _repeatable;
    private readonly NumericUpDown _dispensations;
    private readonly ListView _dispensationList;
    private readonly Button _addDispensation;
    private readonly Button _editDispensation;
    private readonly Button _removeDispensation;
    private readonly Label _hint;
    private readonly Label _error;
    private readonly Guid? _id;
    private readonly DateOnly _today;
    private readonly List<DispensationEntry> _records;
    // As loaded: the editor saves only what the user changed, so a
    // dispensation another device records meanwhile is kept.
    private readonly IReadOnlyList<DispensationEntry> _original;
    private readonly Prescription? _existing;
    private bool _validUntilTouched;

    public PrescriptionEditDialog(
        IReadOnlyList<(Guid Id, string Name)> medicines,
        Guid? selectedMedicineId,
        Prescription? existing,
        IReadOnlyList<PrescriptionDispensation> dispensations,
        DateOnly today,
        ILocalizationService localization,
        bool offerNrePaste = false)
    {
        _loc = localization;
        _id = existing?.Id;
        _today = today;
        _existing = existing;
        _original = [.. dispensations.OrderBy(d => d.CollectedOn)
            .Select(d => new DispensationEntry(d.Id, d.CollectedOn, d.Packages))];
        _records = [.. _original];

        Text = _loc.Get(existing is null ? "Ui.PrescriptionEditDialog.Title.New" : "Ui.PrescriptionEditDialog.Title.Edit");
        Width = 520;
        Height = 680;
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
        // A stored "valid until" equal to the default of its kind still
        // follows the issue date and the kind, so ticking "repeatable" on
        // a single prescription moves it to the repeatable default.
        _validUntilTouched = existing is { ValidUntil: { } until }
            && !(existing.IssuedOn is { } issuedOn && until == (existing.IsRepeatable
                ? PrescriptionRules.DefaultRepeatableValidUntil(issuedOn)
                : PrescriptionRules.DefaultValidUntil(issuedOn)));
        _issued.ValueChanged += (_, _) => PrefillValidUntil();
        _validUntil.ValueChanged += (_, _) => _validUntilTouched = _validUntilTouched || _validUntil.Focused;

        _code = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = PrescriptionRules.MaxCodeLength,
            Text = existing?.Code ?? string.Empty,
        };
        _codeError = DialogLayout.ErrorLabel();
        Control codeField = _code;
        if (offerNrePaste)
        {
            var paste = DialogLayout.Button(_loc.Get("Ui.PrescriptionEditDialog.PasteNre"));
            paste.Click += (_, _) => PasteNre();
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _code.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            row.Controls.Add(_code, 0, 0);
            row.Controls.Add(paste, 1, 0);
            codeField = row;
        }
        _code.TextChanged += (_, _) => DialogLayout.ShowError(_codeError, null);

        _packages = new NumericUpDown
        {
            Minimum = 0,
            Maximum = PrescriptionRules.MaxPackages,
            Value = existing?.Packages ?? 0,
            Width = 80,
            Dock = DockStyle.Left,
        };

        _repeatable = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.PrescriptionEditDialog.Repeatable"),
            Checked = existing?.IsRepeatable == true,
        };
        _dispensations = new NumericUpDown
        {
            Minimum = 2,
            Maximum = PrescriptionRules.MaxDispensations,
            // An imported or synced value may be outside the range the
            // control accepts: clamped, so the editor opens and Validate
            // reports the count on save if it no longer fits.
            Value = existing is { IsRepeatable: true } current
                ? Math.Clamp(current.Dispensations!.Value, 2, PrescriptionRules.MaxDispensations)
                : PrescriptionRules.MaxDispensations,
            Width = 80,
            Dock = DockStyle.Left,
        };
        _dispensationList = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Width = 300,
            Height = 130,
        };
        _dispensationList.Columns.Add(_loc.Get("Ui.PrescriptionEditDialog.Column.CollectedOn"), 150);
        _dispensationList.Columns.Add(_loc.Get("Ui.PrescriptionEditDialog.Column.Packages"), 110);
        _dispensationList.SelectedIndexChanged += (_, _) => UpdateRepeatable();
        _dispensationList.DoubleClick += (_, _) => EditDispensation();
        _addDispensation = DialogLayout.Button(_loc.Get("Ui.PrescriptionEditDialog.Dispensation.Add"));
        _editDispensation = DialogLayout.Button(_loc.Get("Ui.PrescriptionEditDialog.Dispensation.Edit"));
        _removeDispensation = DialogLayout.Button(_loc.Get("Ui.PrescriptionEditDialog.Dispensation.Remove"));
        _addDispensation.Click += (_, _) => AddDispensation();
        _editDispensation.Click += (_, _) => EditDispensation();
        _removeDispensation.Click += (_, _) => RemoveDispensation();
        var dispensationButtons = DialogLayout.Row(_addDispensation, _editDispensation, _removeDispensation);
        var dispensationPanel = DialogLayout.Stack(_dispensationList, dispensationButtons);
        dispensationPanel.Dock = DockStyle.None;
        dispensationPanel.Padding = Padding.Empty;
        _repeatable.CheckedChanged += (_, _) =>
        {
            PrefillValidUntil();
            UpdateRepeatable();
        };

        _hint = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new Size(440, 0),
        };
        _error = DialogLayout.ErrorLabel();

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Medicine"), _medicine);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Requested"), _requested);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Issued"), _issued);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Code"), codeField);
        DialogLayout.AddRow(table, string.Empty, _codeError);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Packages"), _packages);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.ValidUntil"), _validUntil);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Collected"), _collected);
        DialogLayout.AddRow(table, string.Empty, _repeatable);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.Dispensations"), _dispensations);
        DialogLayout.AddRow(table, _loc.Get("Ui.PrescriptionEditDialog.DispensationList"), dispensationPanel);
        DialogLayout.AddRow(table, string.Empty, _hint);
        DialogLayout.AddRow(table, string.Empty, _error);

        var okButton = DialogLayout.Button(_loc.Get("Common.Save"));
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += (_, _) => Confirm();
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
        FillDispensations();
        UpdateRepeatable();
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

    // Until the user sets "valid until" by hand, it follows the issue date
    // with the default validity of the kind of prescription.
    private void PrefillValidUntil()
    {
        if (_validUntilTouched || !_issued.Checked) return;
        var issued = DateOnly.FromDateTime(_issued.Value.Date);
        var until = _repeatable.Checked
            ? PrescriptionRules.DefaultRepeatableValidUntil(issued)
            : PrescriptionRules.DefaultValidUntil(issued);
        _validUntil.Value = until.ToDateTime(TimeOnly.MinValue);
        _validUntil.Checked = true;
    }

    private void UpdateRepeatable()
    {
        var repeatable = _repeatable.Checked;
        _dispensations.Enabled = repeatable;
        _dispensationList.Enabled = repeatable;
        _addDispensation.Enabled = repeatable;
        _editDispensation.Enabled = repeatable && _dispensationList.SelectedItems.Count == 1;
        _removeDispensation.Enabled = repeatable && _dispensationList.SelectedItems.Count == 1;
        _collected.Enabled = !repeatable;
        if (repeatable) _collected.Checked = false;
        _hint.Text = repeatable
            ? _loc.Get("Ui.PrescriptionEditDialog.HintRepeatable", PrescriptionRules.DefaultRepeatableValidityMonths)
            : _loc.Get("Ui.PrescriptionEditDialog.Hint", PrescriptionRules.DefaultValidityDays);
    }

    private void FillDispensations(int? select = null)
    {
        var c = _loc.CurrentCulture;
        _records.Sort((x, y) => x.CollectedOn.CompareTo(y.CollectedOn));
        _dispensationList.BeginUpdate();
        _dispensationList.Items.Clear();
        foreach (var record in _records)
        {
            var row = new ListViewItem(record.CollectedOn.ToString("d", c)) { Tag = record };
            row.SubItems.Add(record.Packages?.ToString(c) ?? string.Empty);
            _dispensationList.Items.Add(row);
        }
        if (select is { } index && index >= 0 && index < _dispensationList.Items.Count)
        {
            _dispensationList.Items[index].Selected = true;
        }
        _dispensationList.EndUpdate();
        UpdateRepeatable();
    }

    private DispensationEntry? SelectedRecord
        => _dispensationList.SelectedItems.Count == 1 ? _dispensationList.SelectedItems[0].Tag as DispensationEntry : null;

    private void AddDispensation()
    {
        using var dialog = new DispensationEditDialog(_today, null, isNew: true, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var record = new DispensationEntry(null, dialog.CollectedOn, dialog.Packages);
        _records.Add(record);
        FillDispensations();
        Select(record);
    }

    private void EditDispensation()
    {
        if (SelectedRecord is not { } current) return;
        using var dialog = new DispensationEditDialog(current.CollectedOn, current.Packages, isNew: false, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var changed = current with { CollectedOn = dialog.CollectedOn, Packages = dialog.Packages };
        _records[_records.IndexOf(current)] = changed;
        FillDispensations();
        Select(changed);
    }

    private void RemoveDispensation()
    {
        if (SelectedRecord is not { } current) return;
        _records.Remove(current);
        FillDispensations();
    }

    private void Select(DispensationEntry record)
    {
        foreach (ListViewItem item in _dispensationList.Items)
        {
            item.Selected = ReferenceEquals(item.Tag, record);
        }
    }

    // The clipboard is read here only, on the user's click. Nothing of it
    // is logged.
    private void PasteNre()
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard.
            text = string.Empty;
        }
        if (!NreCode.TryParse(text, out var code))
        {
            DialogLayout.ShowError(_codeError, _loc.Get("Ui.PrescriptionEditDialog.Error.NotNre"), _code);
            return;
        }
        _code.Text = code.Value;
        DialogLayout.ShowError(_codeError, null);
        if (!_issued.Checked)
        {
            _issued.Value = _today.ToDateTime(TimeOnly.MinValue);
            _issued.Checked = true;
            PrefillValidUntil();
        }
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
            CollectedOn = _repeatable.Checked ? null : DateOf(_collected),
            Dispensations = _repeatable.Checked ? (int)_dispensations.Value : null,
        };
        // Only the dispensations added or changed are sent, with the ids
        // removed. A single prescription keeps no dispensation: unticking
        // "repeatable" with dispensations listed is reported, not discarded.
        var edits = _records
            .Where(r => r.Id is null || _original.FirstOrDefault(o => o.Id == r.Id) is not { } o || o != r)
            .ToList();
        var removed = _original
            .Where(o => _records.All(r => r.Id != o.Id))
            .Select(o => o.Id!.Value)
            .ToList();
        // Same rule as SavePrescription: the dates of every dispensation
        // are checked when the issue date, "valid until" or the number
        // allowed changed, else only those edited, the others counted.
        var datesChanged = _existing is null
            || _existing.IssuedOn != candidate.IssuedOn
            || _existing.ValidUntil != candidate.ValidUntil
            || _existing.Dispensations != candidate.Dispensations;
        var checkedRecords = (datesChanged ? _records : edits).Select(r => new PrescriptionDispensation
        {
            PrescriptionId = candidate.Id,
            MedicineId = candidate.MedicineId,
            CollectedOn = r.CollectedOn,
            Packages = r.Packages,
        }).ToList();
        if (PrescriptionRules.Validate(candidate, checkedRecords, _records.Count - checkedRecords.Count) is { } error)
        {
            DialogLayout.ShowError(_error, _loc.Get("Ui.PrescriptionEditDialog.Error." + error));
            return;
        }
        Result = new SavePrescriptionCommand(_id, candidate.MedicineId, candidate.RequestedOn, candidate.IssuedOn,
            candidate.Code, candidate.Packages, candidate.ValidUntil, candidate.CollectedOn,
            candidate.Dispensations, edits, removed);
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record MedicineOption(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
}
