using System.Windows.Forms;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;

namespace MedReminder.UI.Forms;

// Dialog usato sia per "nuova medicina" (Mode=Create) sia per "modifica"
// (Mode=Edit). Al termine espone Result: null se l'utente annulla,
// altrimenti un DTO con i campi validi. La persistenza avviene nel
// chiamante (MainForm) invocando il use case appropriato.
internal sealed class MedicineEditDialog : MedReminderFormBase
{
    public enum EditMode { Create, Edit }

    public MedicineEditResult? Result { get; private set; }

    private readonly TextBox _nameBox;
    private readonly TextBox _ingredientBox;
    private readonly TextBox _packageBox;
    private readonly ComboBox _unitBox;
    private readonly NumericUpDown _doseBox;
    private readonly NumericUpDown _adminPerDayBox;
    private readonly DateTimePicker _startDatePicker;
    private readonly CheckBox _hasEndDate;
    private readonly DateTimePicker _endDatePicker;
    private readonly NumericUpDown _thresholdBox;
    private readonly TextBox _doctorBox;
    private readonly TextBox _notesBox;
    private readonly NumericUpDown _initialQtyBox;
    private readonly CheckBox _channelWindows;
    private readonly CheckBox _channelEmail;
    private readonly CheckBox _isActiveBox;
    private readonly ListView _slotsList;
    private readonly Label _slotsSummary;
    private readonly List<AdministrationSlotEntry> _slots = new();
    private readonly EditMode _mode;

    public MedicineEditDialog(EditMode mode, MedicineEditResult? seed = null)
    {
        _mode = mode;
        Text = mode == EditMode.Create ? "Nuova medicina" : "Modifica medicina";
        Width = 620;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _nameBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _ingredientBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _packageBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _unitBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _unitBox.Items.AddRange(new object[] { "compresse", "capsule", "bustine", "ml", "dosi", "flaconi" });
        _doseBox = MakeDecimalUpDown(0.01m, 1000m, 2, initial: 1m);
        _adminPerDayBox = MakeIntUpDown(1, 24, initial: 2);
        _startDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Value = DateTime.Today };
        _hasEndDate = new CheckBox { Text = "Con data di fine terapia", AutoSize = true };
        _endDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Enabled = false, Value = DateTime.Today.AddMonths(1) };
        _hasEndDate.CheckedChanged += (_, _) => _endDatePicker.Enabled = _hasEndDate.Checked;
        _thresholdBox = MakeIntUpDown(0, 365, initial: 7);
        _doctorBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _notesBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical, MaxLength = 1000 };
        _initialQtyBox = MakeDecimalUpDown(0m, 100000m, 2, initial: 0m);
        _channelWindows = new CheckBox { Text = "Notifica Windows", AutoSize = true, Checked = true };
        _channelEmail = new CheckBox { Text = "Email", AutoSize = true, Checked = false };
        _isActiveBox = new CheckBox { Text = "Attiva", AutoSize = true, Checked = true };

        _slotsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = true,
            Height = 140,
        };
        _slotsList.Columns.Add("Ora", 80);
        _slotsList.Columns.Add("Dose", 80);
        _slotsList.Columns.Add("Descrizione", 300);
        _slotsList.DoubleClick += (_, _) => EditSelectedSlot();
        _slotsSummary = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
            Text = string.Empty,
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

        AddRow(table, "Nome*", _nameBox);
        AddRow(table, "Principio attivo", _ingredientBox);
        AddRow(table, "Confezione", _packageBox);
        AddRow(table, "Unità*", _unitBox);
        AddRow(table, "Dose per somministrazione*", _doseBox);
        AddRow(table, "Somministrazioni al giorno*", _adminPerDayBox);
        AddRow(table, "Data inizio terapia*", _startDatePicker);
        AddRow(table, string.Empty, _hasEndDate);
        AddRow(table, "Data fine terapia", _endDatePicker);
        AddRow(table, "Soglia avviso (giorni)*", _thresholdBox);
        AddRow(table, "Medico di riferimento", _doctorBox);
        AddRow(table, "Note", _notesBox);
        if (_mode == EditMode.Create)
        {
            AddRow(table, "Quantità iniziale in scorta", _initialQtyBox);
        }
        AddRow(table, "Canali di notifica", BuildChannelsPanel());
        if (_mode == EditMode.Edit)
        {
            AddRow(table, string.Empty, _isActiveBox);
        }

        AddRow(table, "Orari (opzionale)", BuildSlotsPanel());
        AddRow(table, string.Empty, _slotsSummary);
        UpdateSlotsSummary();

        var okButton = new Button { Text = "Salva", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        okButton.Click += OnConfirmClick;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(table);

        Controls.Add(scroll);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        if (seed is not null) ApplySeed(seed);
    }

    private void ApplySeed(MedicineEditResult seed)
    {
        _nameBox.Text = seed.Name;
        _ingredientBox.Text = seed.ActiveIngredient ?? string.Empty;
        _packageBox.Text = seed.Package ?? string.Empty;
        _unitBox.Text = seed.Unit;
        _doseBox.Value = seed.DosePerAdministration;
        _adminPerDayBox.Value = seed.AdministrationsPerDay;
        _startDatePicker.Value = seed.StartDate.ToDateTime(TimeOnly.MinValue);
        _hasEndDate.Checked = seed.EndDate.HasValue;
        _endDatePicker.Enabled = seed.EndDate.HasValue;
        if (seed.EndDate is { } end) _endDatePicker.Value = end.ToDateTime(TimeOnly.MinValue);
        _thresholdBox.Value = seed.ThresholdDays;
        _doctorBox.Text = seed.DoctorName ?? string.Empty;
        _notesBox.Text = seed.Notes ?? string.Empty;
        _initialQtyBox.Value = seed.InitialQuantity;
        _channelWindows.Checked = (seed.NotificationChannels & NotificationChannels.Windows) != 0;
        _channelEmail.Checked = (seed.NotificationChannels & NotificationChannels.Email) != 0;
        _isActiveBox.Checked = seed.IsActive;

        _slots.Clear();
        if (seed.Slots is not null)
        {
            _slots.AddRange(seed.Slots);
        }
        RefreshSlotsList();
    }

    private void OnConfirmClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Il nome è obbligatorio.", "Dati mancanti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitBox.Text))
        {
            MessageBox.Show(this, "L'unità è obbligatoria.", "Dati mancanti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (_hasEndDate.Checked && _endDatePicker.Value.Date < _startDatePicker.Value.Date)
        {
            MessageBox.Show(this, "La data di fine non può precedere l'inizio.", "Dati incoerenti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var channels = NotificationChannels.None;
        if (_channelWindows.Checked) channels |= NotificationChannels.Windows;
        if (_channelEmail.Checked) channels |= NotificationChannels.Email;

        Result = new MedicineEditResult(
            Name: _nameBox.Text.Trim(),
            ActiveIngredient: NullIfBlank(_ingredientBox.Text),
            Package: NullIfBlank(_packageBox.Text),
            Unit: _unitBox.Text.Trim(),
            DosePerAdministration: _doseBox.Value,
            AdministrationsPerDay: (int)_adminPerDayBox.Value,
            StartDate: DateOnly.FromDateTime(_startDatePicker.Value.Date),
            EndDate: _hasEndDate.Checked ? DateOnly.FromDateTime(_endDatePicker.Value.Date) : null,
            ThresholdDays: (int)_thresholdBox.Value,
            DoctorName: NullIfBlank(_doctorBox.Text),
            Notes: NullIfBlank(_notesBox.Text),
            InitialQuantity: _initialQtyBox.Value,
            NotificationChannels: channels,
            IsActive: _isActiveBox.Checked,
            Slots: _slots.ToList());
    }

    private Control BuildChannelsPanel()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_channelWindows);
        panel.Controls.Add(_channelEmail);
        return panel;
    }

    private Control BuildSlotsPanel()
    {
        var addButton = new Button { Text = "Aggiungi…", AutoSize = true, Height = 26 };
        var editButton = new Button { Text = "Modifica…", AutoSize = true, Height = 26 };
        var removeButton = new Button { Text = "Rimuovi", AutoSize = true, Height = 26 };
        addButton.Click += (_, _) => AddSlot();
        editButton.Click += (_, _) => EditSelectedSlot();
        removeButton.Click += (_, _) => RemoveSelectedSlot();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(addButton);
        buttons.Controls.Add(editButton);
        buttons.Controls.Add(removeButton);

        var container = new Panel { Height = 180, Width = 400 };
        _slotsList.Dock = DockStyle.Fill;
        container.Controls.Add(_slotsList);
        container.Controls.Add(buttons);
        return container;
    }

    private void AddSlot()
    {
        using var dialog = new AdministrationSlotDialog(
            unit: _unitBox.Text.Trim().Length == 0 ? "unità" : _unitBox.Text.Trim(),
            suggestedDose: _doseBox.Value);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _slots.Add(dialog.Result);
        RefreshSlotsList();
    }

    private void EditSelectedSlot()
    {
        var index = SelectedSlotIndex();
        if (index < 0) return;
        using var dialog = new AdministrationSlotDialog(
            unit: _unitBox.Text.Trim().Length == 0 ? "unità" : _unitBox.Text.Trim(),
            suggestedDose: _doseBox.Value,
            seed: _slots[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _slots[index] = dialog.Result;
        RefreshSlotsList();
    }

    private void RemoveSelectedSlot()
    {
        var index = SelectedSlotIndex();
        if (index < 0) return;
        _slots.RemoveAt(index);
        RefreshSlotsList();
    }

    private int SelectedSlotIndex()
    {
        return _slotsList.SelectedIndices.Count == 0 ? -1 : _slotsList.SelectedIndices[0];
    }

    private void RefreshSlotsList()
    {
        _slotsList.BeginUpdate();
        _slotsList.Items.Clear();
        foreach (var slot in _slots)
        {
            var row = new ListViewItem(slot.TimeDisplay);
            row.SubItems.Add(slot.Dose.ToString("0.##"));
            row.SubItems.Add(slot.LabelDisplay);
            _slotsList.Items.Add(row);
        }
        _slotsList.EndUpdate();
        UpdateSlotsSummary();
    }

    private void UpdateSlotsSummary()
    {
        if (_slots.Count == 0)
        {
            _slotsSummary.Text = "Nessuno slot configurato: la medicina userà "
                + "\"dose × somministrazioni al giorno\" indicato sopra.";
            return;
        }
        var total = _slots.Sum(s => s.Dose);
        var unit = _unitBox.Text.Trim().Length == 0 ? "unità" : _unitBox.Text.Trim();
        _slotsSummary.Text = $"{_slots.Count} slot definiti — totale giornaliero: "
            + $"{total:0.##} {unit}. Il consumo giornaliero sarà calcolato da questi orari.";
    }

    private static NumericUpDown MakeDecimalUpDown(decimal min, decimal max, int decimals, decimal initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = 0.5m,
            Value = initial,
            Dock = DockStyle.Left,
            Width = 120,
        };

    private static NumericUpDown MakeIntUpDown(int min, int max, int initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = 0,
            Increment = 1,
            Value = initial,
            Dock = DockStyle.Left,
            Width = 120,
        };

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
}

// Trasporta dati tra dialog e caller in entrambi i sensi (seed per Edit).
// Immutabile lato dialog: l'utente ottiene una nuova instance dopo Save.
internal sealed record MedicineEditResult(
    string Name,
    string? ActiveIngredient,
    string? Package,
    string Unit,
    decimal DosePerAdministration,
    int AdministrationsPerDay,
    DateOnly StartDate,
    DateOnly? EndDate,
    int ThresholdDays,
    string? DoctorName,
    string? Notes,
    decimal InitialQuantity,
    NotificationChannels NotificationChannels,
    bool IsActive,
    IReadOnlyList<AdministrationSlotEntry>? Slots = null)
{
    public AddMedicineCommand ToAddCommand() => new(
        Name: Name,
        Unit: Unit,
        DosePerAdministration: DosePerAdministration,
        AdministrationsPerDay: AdministrationsPerDay,
        StartDate: StartDate,
        ThresholdDays: ThresholdDays,
        NotificationChannels: NotificationChannels,
        ActiveIngredient: ActiveIngredient,
        Package: Package,
        EndDate: EndDate,
        DoctorName: DoctorName,
        Notes: Notes,
        InitialQuantity: InitialQuantity,
        AdministrationSlots: MapSlots());

    // Passa sempre gli slot (anche vuoti): il use case UpdateMedicine
    // distingue null=lascia-come-sono vs [] = azzera. Qui l'utente ha
    // esplicitamente confermato la lista corrente, quindi vogliamo che
    // venga applicata (sostituzione atomica).
    public UpdateMedicineCommand ToUpdateCommand(Guid id) => new(
        MedicineId: id,
        Name: Name,
        ActiveIngredient: ActiveIngredient,
        Package: Package,
        Unit: Unit,
        ThresholdDays: ThresholdDays,
        NotificationChannels: NotificationChannels,
        EndDate: EndDate,
        DoctorName: DoctorName,
        Notes: Notes,
        IsActive: IsActive,
        AdministrationSlots: MapSlots() ?? Array.Empty<AdministrationSlotInput>());

    private IReadOnlyList<AdministrationSlotInput>? MapSlots()
    {
        if (Slots is null || Slots.Count == 0) return null;
        return Slots.Select(s => new AdministrationSlotInput(s.Dose, s.Time, s.TimingLabel)).ToList();
    }
}
