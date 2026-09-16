using System.Windows.Forms;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Editor di UN singolo slot di somministrazione (Incremento 10).
// Usato dal MedicineEditDialog per aggiungere/modificare una riga.
// L'utente può indicare orario esatto opzionale + descrizione libera
// (con dropdown di preset comuni: "al mattino", "dopo cena", ecc.).
internal sealed class AdministrationSlotDialog : MedReminderFormBase
{
    // Chiavi dei preset localizzati: la ComboBox mostra i testi tradotti
    // per la lingua corrente.
    private static readonly string[] PresetKeys =
    {
        "Ui.AdministrationSlotDialog.Preset.Morning",
        "Ui.AdministrationSlotDialog.Preset.MorningEmptyStomach",
        "Ui.AdministrationSlotDialog.Preset.BeforeBreakfast",
        "Ui.AdministrationSlotDialog.Preset.AfterBreakfast",
        "Ui.AdministrationSlotDialog.Preset.MidMorning",
        "Ui.AdministrationSlotDialog.Preset.BeforeLunch",
        "Ui.AdministrationSlotDialog.Preset.AfterLunch",
        "Ui.AdministrationSlotDialog.Preset.Afternoon",
        "Ui.AdministrationSlotDialog.Preset.BeforeDinner",
        "Ui.AdministrationSlotDialog.Preset.AfterDinner",
        "Ui.AdministrationSlotDialog.Preset.BeforeSleep",
        "Ui.AdministrationSlotDialog.Preset.Night",
        "Ui.AdministrationSlotDialog.Preset.AsNeeded",
    };

    public AdministrationSlotEntry? Result { get; private set; }

    private readonly ILocalizationService _loc;
    private readonly CheckBox _hasTime;
    private readonly DateTimePicker _timePicker;
    private readonly NumericUpDown _doseBox;
    private readonly ComboBox _labelBox;

    public AdministrationSlotDialog(
        string unit, decimal suggestedDose, ILocalizationService localization,
        AdministrationSlotEntry? seed = null)
    {
        _loc = localization;
        Text = _loc.Get(seed is null
            ? "Ui.AdministrationSlotDialog.Title.New"
            : "Ui.AdministrationSlotDialog.Title.Edit");
        Width = 500;
        Height = 340;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        _hasTime = new CheckBox
        {
            Text = _loc.Get("Ui.AdministrationSlotDialog.HasTime"),
            AutoSize = true,
            Checked = seed?.Time is not null,
        };
        _timePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Dock = DockStyle.Left,
            Width = 100,
            Value = seed?.Time is { } t ? DateTime.Today.Add(t.ToTimeSpan()) : DateTime.Today.AddHours(8),
            Enabled = seed?.Time is not null,
        };
        _hasTime.CheckedChanged += (_, _) => _timePicker.Enabled = _hasTime.Checked;

        _doseBox = new NumericUpDown
        {
            Minimum = 0.01m,
            Maximum = 1000m,
            DecimalPlaces = 2,
            Increment = 0.5m,
            Value = seed?.Dose ?? (suggestedDose > 0m ? suggestedDose : 1m),
            Dock = DockStyle.Left,
            Width = 100,
        };

        _labelBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (var key in PresetKeys)
        {
            _labelBox.Items.Add(_loc.Get(key));
        }
        _labelBox.Text = seed?.TimingLabel ?? string.Empty;

        var note = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
            MaximumSize = new System.Drawing.Size(440, 0),
            Text = _loc.Get("Ui.AdministrationSlotDialog.Note"),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Time"), BuildTimeRow());
        AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Dose"), BuildDoseRow(unit));
        AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Description"), _labelBox);
        AddRow(table, string.Empty, note);

        var okButton = new Button { Text = _loc.Get("Ui.AdministrationSlotDialog.Save"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
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

    private Control BuildTimeRow()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_hasTime);
        panel.Controls.Add(_timePicker);
        return panel;
    }

    private Control BuildDoseRow(string unit)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        panel.Controls.Add(_doseBox);
        panel.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(6, 8, 4, 4) });
        return panel;
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var hasTime = _hasTime.Checked;
        var hasLabel = !string.IsNullOrWhiteSpace(_labelBox.Text);
        if (!hasTime && !hasLabel)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.AdministrationSlotDialog.Validation.NeedOne"),
                _loc.Get("Ui.MedicineEditDialog.MissingData.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        TimeOnly? time = hasTime ? TimeOnly.FromDateTime(_timePicker.Value) : null;
        var label = hasLabel ? _labelBox.Text.Trim() : null;
        Result = new AdministrationSlotEntry(time, _doseBox.Value, label);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }
}

// Riga di slot mantenuta dal MedicineEditDialog. È il DTO UI-side che
// viene poi tradotto in AdministrationSlotInput per il use case.
internal sealed record AdministrationSlotEntry(TimeOnly? Time, decimal Dose, string? TimingLabel)
{
    public string TimeDisplay => Time?.ToString("HH:mm") ?? "—";

    public string LabelDisplay => string.IsNullOrEmpty(TimingLabel) ? "—" : TimingLabel;
}
