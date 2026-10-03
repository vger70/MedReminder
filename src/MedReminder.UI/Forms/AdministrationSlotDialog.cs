using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Editor for ONE administration slot (Increment 10). Used by
// MedicineEditDialog to add / edit a single row. The user can
// provide an optional exact time + free-form description (with a
// dropdown of common presets: "in the morning", "after dinner",
// etc.).
internal sealed class AdministrationSlotDialog : MedReminderFormBase
{
    // Keys for the localized presets: the ComboBox shows the
    // translated texts for the current language.
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

    private readonly Label _error = DialogLayout.ErrorLabel();
    private readonly ILocalizationService _loc;
    private readonly CheckBox _hasTime;
    private readonly DateTimePicker _timePicker;
    private readonly NumericUpDown _doseBox;
    private readonly ComboBox _labelBox;
    private readonly CheckBox _asNeeded;

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
        DialogLayout.GrowToContent(this);

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

        // As-needed dose: never consumed automatically
        // (ANALYSIS-INTRADAY-CONSUMPTION.md §5.1). Picking the "As
        // needed" preset ticks it.
        _asNeeded = new CheckBox
        {
            Text = _loc.Get("Ui.AdministrationSlotDialog.AsNeeded"),
            AutoSize = true,
            Checked = seed?.IsAsNeeded ?? false,
        };
        var asNeededPreset = _loc.Get("Ui.AdministrationSlotDialog.Preset.AsNeeded");
        _labelBox.SelectedIndexChanged += (_, _) =>
        {
            if (string.Equals(_labelBox.Text, asNeededPreset, StringComparison.Ordinal)) _asNeeded.Checked = true;
        };

        var note = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
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
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        DialogLayout.AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Time"), BuildTimeRow());
        DialogLayout.AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Dose"), BuildDoseRow(unit));
        DialogLayout.AddRow(table, _loc.Get("Ui.AdministrationSlotDialog.Row.Description"), _labelBox);
        DialogLayout.AddRow(table, string.Empty, _asNeeded);
        DialogLayout.AddRow(table, string.Empty, _error);
        DialogLayout.AddRow(table, string.Empty, note);

        var okButton = DialogLayout.Button(_loc.Get("Ui.AdministrationSlotDialog.Save"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += OnConfirm;

        var buttonPanel = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttonPanel);
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
            // Shown under the fields instead of a message box (F9).
            DialogLayout.ShowError(_error, _loc.Get("Ui.AdministrationSlotDialog.Validation.NeedOne"), _labelBox);
            DialogResult = DialogResult.None;
            return;
        }

        TimeOnly? time = hasTime ? TimeOnly.FromDateTime(_timePicker.Value) : null;
        var label = hasLabel ? _labelBox.Text.Trim() : null;
        Result = new AdministrationSlotEntry(time, _doseBox.Value, label, _asNeeded.Checked);
    }

}

// Slot row kept by MedicineEditDialog. UI-side DTO that gets
// translated into AdministrationSlotInput for the use case.
internal sealed record AdministrationSlotEntry(
    TimeOnly? Time, decimal Dose, string? TimingLabel, bool IsAsNeeded = false)
{
    public string TimeDisplay => Time?.ToString("HH:mm") ?? "—";

    public string LabelDisplay => string.IsNullOrEmpty(TimingLabel) ? "—" : TimingLabel;
}
