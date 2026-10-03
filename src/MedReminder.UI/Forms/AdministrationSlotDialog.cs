using MedReminder.Application.Abstractions;
using MedReminder.Application.DoseTimes;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Editor for ONE administration slot (Increment 10). Used by
// MedicineEditDialog to add / edit a single row. The user can
// provide an optional exact time + free-form description (with a
// dropdown of the profile's time-of-day presets: "in the morning",
// "after dinner", etc., docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §6). A description equal to a preset name links the slot to that
// preset (PresetId); a typed one does not.
internal sealed class AdministrationSlotDialog : MedReminderFormBase
{
    public AdministrationSlotEntry? Result { get; private set; }

    private readonly IReadOnlyList<(EffectiveDoseTimePreset Preset, string Name)> _presets;
    private readonly AdministrationSlotEntry? _seed;
    private readonly Label _presetTime;

    private readonly Label _error = DialogLayout.ErrorLabel();
    private readonly ILocalizationService _loc;
    private readonly CheckBox _hasTime;
    private readonly DateTimePicker _timePicker;
    private readonly NumericUpDown _doseBox;
    private readonly ComboBox _labelBox;
    private readonly CheckBox _asNeeded;

    public AdministrationSlotDialog(
        string unit, decimal suggestedDose, ILocalizationService localization,
        AdministrationSlotEntry? seed = null, DoseTimeSettings? doseTimes = null)
    {
        _loc = localization;
        _seed = seed;
        _presets = [.. (doseTimes ?? DoseTimeSettings.BuiltIn).Presets
            .Select(p => (p, p.BuiltInKey is { } key
                ? _loc.Get("Ui.AdministrationSlotDialog.Preset." + key)
                : p.Label ?? string.Empty))];
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
        foreach (var (preset, name) in _presets)
        {
            if (!preset.IsHidden) _labelBox.Items.Add(name);
        }
        _labelBox.Text = seed?.TimingLabel ?? string.Empty;

        // Time of day of the chosen preset, used when the slot has no
        // time of its own.
        _presetTime = new Label { AutoSize = true, ForeColor = UiColors.Hint };

        // As-needed dose: never consumed automatically
        // (ANALYSIS-INTRADAY-CONSUMPTION.md §5.1). Picking the "As
        // needed" preset ticks it.
        _asNeeded = new CheckBox
        {
            Text = _loc.Get("Ui.AdministrationSlotDialog.AsNeeded"),
            AutoSize = true,
            Checked = seed?.IsAsNeeded ?? false,
        };
        _labelBox.SelectedIndexChanged += (_, _) =>
        {
            if (PresetFor(_labelBox.Text) is { IsAsNeeded: true }) _asNeeded.Checked = true;
        };
        _labelBox.TextChanged += (_, _) => UpdatePresetTime();
        UpdatePresetTime();

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
        DialogLayout.AddRow(table, string.Empty, _presetTime);
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
        // An unchanged description keeps its preset, even when it was
        // stored in another language.
        var presetId = label is not null && string.Equals(label, _seed?.TimingLabel, StringComparison.Ordinal)
            ? _seed?.PresetId ?? PresetFor(label)?.Id
            : PresetFor(label)?.Id;
        Result = new AdministrationSlotEntry(time, _doseBox.Value, label, _asNeeded.Checked, presetId);
    }

    private EffectiveDoseTimePreset? PresetFor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        foreach (var (preset, name) in _presets)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)) return preset;
        }
        return null;
    }

    private void UpdatePresetTime()
    {
        var preset = PresetFor(_labelBox.Text);
        _presetTime.Text = preset?.Time is { } t
            ? _loc.Get("Ui.AdministrationSlotDialog.PresetTime", t.ToString("HH:mm"))
            : string.Empty;
        _presetTime.Visible = _presetTime.Text.Length > 0;
    }

}

// Slot row kept by MedicineEditDialog. UI-side DTO that gets
// translated into AdministrationSlotInput for the use case.
internal sealed record AdministrationSlotEntry(
    TimeOnly? Time, decimal Dose, string? TimingLabel, bool IsAsNeeded = false, Guid? PresetId = null)
{
    public string TimeDisplay => Time?.ToString("HH:mm") ?? "—";

    public string LabelDisplay => string.IsNullOrEmpty(TimingLabel) ? "—" : TimingLabel;
}
