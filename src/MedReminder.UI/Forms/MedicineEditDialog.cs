using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;

namespace MedReminder.UI.Forms;

// Dialog used both for "new medicine" (Mode=Create) and for "edit"
// (Mode=Edit). At the end it exposes Result: null if the user
// cancels, otherwise a DTO with the valid fields. Persistence is
// performed by the caller (MainForm) by invoking the appropriate
// use case.
internal sealed class MedicineEditDialog : MedReminderFormBase
{
    public enum EditMode { Create, Edit }

    public MedicineEditResult? Result { get; private set; }

    private readonly ILocalizationService _loc;
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

    public MedicineEditDialog(EditMode mode, ILocalizationService localization, MedicineEditResult? seed = null)
    {
        _loc = localization;
        _mode = mode;
        Text = _loc.Get(mode == EditMode.Create
            ? "Ui.MedicineEditDialog.Title.New"
            : "Ui.MedicineEditDialog.Title.Edit");
        Width = 620;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _nameBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _ingredientBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _packageBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _unitBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _unitBox.Items.AddRange(new object[]
        {
            _loc.Get("Ui.MedicineEditDialog.Unit.Tablets"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Capsules"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Sachets"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Ml"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Doses"),
            _loc.Get("Ui.MedicineEditDialog.Unit.Vials"),
        });
        _doseBox = MakeDecimalUpDown(0.01m, 1000m, 2, initial: 1m);
        _adminPerDayBox = MakeIntUpDown(1, 24, initial: 2);
        _startDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Value = DateTime.Today };
        _hasEndDate = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.HasEndDateFull"), AutoSize = true };
        _endDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill, Enabled = false, Value = DateTime.Today.AddMonths(1) };
        _hasEndDate.CheckedChanged += (_, _) => _endDatePicker.Enabled = _hasEndDate.Checked;
        _thresholdBox = MakeIntUpDown(0, 365, initial: 7);
        _doctorBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 200 };
        _notesBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical, MaxLength = 1000 };
        _initialQtyBox = MakeDecimalUpDown(0m, 100000m, 2, initial: 0m);
        _channelWindows = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.NotifyWindowsShort"), AutoSize = true, Checked = true };
        _channelEmail = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.NotifyEmailShort"), AutoSize = true, Checked = false };
        _isActiveBox = new CheckBox { Text = _loc.Get("Ui.MedicineEditDialog.Field.IsActive"), AutoSize = true, Checked = true };

        _slotsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = true,
            Height = 140,
        };
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnTime"), 80);
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnDose"), 80);
        _slotsList.Columns.Add(_loc.Get("Ui.MedicineEditDialog.Slots.ColumnLabel"), 300);
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

        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Name"), _nameBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.ActiveIngredient"), _ingredientBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Package"), _packageBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Unit"), _unitBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.DosePerAdmin"), _doseBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.AdminsPerDay"), _adminPerDayBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.StartDateFull"), _startDatePicker);
        AddRow(table, string.Empty, _hasEndDate);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.EndDateFull"), _endDatePicker);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.WarnThreshold"), _thresholdBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.DoctorFull"), _doctorBox);
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Notes"), _notesBox);
        if (_mode == EditMode.Create)
        {
            AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.InitialStock"), _initialQtyBox);
        }
        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.Channels"), BuildChannelsPanel());
        if (_mode == EditMode.Edit)
        {
            AddRow(table, string.Empty, _isActiveBox);
        }

        AddRow(table, _loc.Get("Ui.MedicineEditDialog.Field.SlotsOptional"), BuildSlotsPanel());
        AddRow(table, string.Empty, _slotsSummary);
        UpdateSlotsSummary();

        var okButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Save"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var cancelButton = new Button { Text = _loc.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
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
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.Validation.NameRequired"),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitBox.Text))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.Validation.UnitRequired"),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }
        if (_hasEndDate.Checked && _endDatePicker.Value.Date < _startDatePicker.Value.Date)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MedicineEditDialog.EndBeforeStart"),
                _loc.Get("Ui.MedicineEditDialog.InconsistentData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        var addButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.AddButton"), AutoSize = true, Height = 26 };
        var editButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.EditButton"), AutoSize = true, Height = 26 };
        var removeButton = new Button { Text = _loc.Get("Ui.MedicineEditDialog.Slots.RemoveButton"), AutoSize = true, Height = 26 };
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

    private string EffectiveUnit()
    {
        var t = _unitBox.Text.Trim();
        return t.Length == 0 ? _loc.Get("Ui.MedicineEditDialog.DefaultUnit") : t;
    }

    private void AddSlot()
    {
        using var dialog = new AdministrationSlotDialog(EffectiveUnit(), _doseBox.Value, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _slots.Add(dialog.Result);
        RefreshSlotsList();
    }

    private void EditSelectedSlot()
    {
        var index = SelectedSlotIndex();
        if (index < 0) return;
        using var dialog = new AdministrationSlotDialog(EffectiveUnit(), _doseBox.Value, _loc, _slots[index]);
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
            _slotsSummary.Text = _loc.Get("Ui.MedicineEditDialog.Slots.SummaryNone");
            return;
        }
        var total = _slots.Sum(s => s.Dose);
        _slotsSummary.Text = _loc.Get("Ui.MedicineEditDialog.Slots.Summary",
            _slots.Count, total.ToString("0.##"), EffectiveUnit());
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

// Carries data between the dialog and the caller in both directions
// (seed for Edit). Immutable on the dialog side: the user gets a new
// instance after Save.
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

    // Always pass the slots (even empty): the UpdateMedicine use case
    // distinguishes null=leave-as-is vs [] = clear. Here the user has
    // explicitly confirmed the current list, so we want it applied
    // (atomic replacement).
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
