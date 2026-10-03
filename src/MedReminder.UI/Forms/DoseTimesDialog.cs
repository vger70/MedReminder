using MedReminder.Application.Abstractions;
using MedReminder.Application.DoseTimes;
using MedReminder.Domain.Medicines;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Therapy → Dose times… (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §6, §9): the time of day of each time-of-day preset ("In the morning"
// = 08:00) and the times of medicines without slots. Display only: the
// times place doses in the day and never change stored stock. Works on a
// copy; the caller saves Result.
internal sealed class DoseTimesDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly List<EffectiveDoseTimePreset> _presets;
    private readonly ListView _list;
    private readonly Button _editButton;
    private readonly Button _removeButton;
    private readonly Dictionary<int, TextBox> _defaultBoxes = new();
    private readonly Label _error = DialogLayout.ErrorLabel();

    public DoseTimeSettings? Result { get; private set; }

    public DoseTimesDialog(DoseTimeSettings settings, ILocalizationService localization)
    {
        _loc = localization;
        _presets = [.. settings.Presets];

        Text = _loc.Get("Ui.DoseTimesDialog.Title");
        Width = 720;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 64,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            Text = _loc.Get("Ui.DoseTimesDialog.Hint"),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.DoseTimesDialog.Column.Name"), 300);
        _list.Columns.Add(_loc.Get("Ui.DoseTimesDialog.Column.Time"), 90);
        _list.Columns.Add(_loc.Get("Ui.DoseTimesDialog.Column.AsNeeded"), 110);
        _list.Columns.Add(_loc.Get("Ui.DoseTimesDialog.Column.Shown"), 90);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => Edit();

        var defaults = DialogLayout.FormTable();
        defaults.Dock = DockStyle.Bottom;
        DialogLayout.AddRow(defaults, string.Empty, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Text = _loc.Get("Ui.DoseTimesDialog.Defaults.Hint"),
        });
        for (var n = 1; n <= DoseTimeDefault.MaxAdministrations; n++)
        {
            var box = new TextBox { Width = 220, Text = DoseTimeDefault.Format(settings.Defaults[n]).Replace(";", "; ") };
            _defaultBoxes[n] = box;
            DialogLayout.AddRow(defaults, _loc.Get("Ui.DoseTimesDialog.Defaults.PerDay", n), box);
        }
        DialogLayout.AddRow(defaults, string.Empty, _error);

        var addButton = DialogLayout.Button(_loc.Get("Ui.DoseTimesDialog.Add"));
        _editButton = DialogLayout.Button(_loc.Get("Ui.DoseTimesDialog.Edit"));
        _removeButton = DialogLayout.Button(_loc.Get("Ui.DoseTimesDialog.Remove"));
        var saveButton = DialogLayout.Button(_loc.Get("Common.Save"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        addButton.Click += (_, _) => Add();
        _editButton.Click += (_, _) => Edit();
        _removeButton.Click += (_, _) => Remove();
        saveButton.Click += OnSave;
        var buttons = DialogLayout.ButtonBar(this, saveButton, cancelButton, _removeButton, _editButton, addButton);

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(defaults);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        Refill();
    }

    private string NameOf(EffectiveDoseTimePreset p)
        => p.BuiltInKey is { } key ? _loc.Get("Ui.AdministrationSlotDialog.Preset." + key) : p.Label ?? string.Empty;

    private EffectiveDoseTimePreset? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as EffectiveDoseTimePreset : null;

    private void UpdateButtons()
    {
        _editButton.Enabled = Selected is not null;
        _removeButton.Enabled = Selected is { IsBuiltIn: false };
    }

    private void Refill(Guid? select = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var p in _presets)
        {
            var row = new ListViewItem(NameOf(p)) { Tag = p };
            row.SubItems.Add(p.Time?.ToString("HH:mm") ?? "—");
            row.SubItems.Add(p.IsAsNeeded ? _loc.Get("Common.Yes") : string.Empty);
            row.SubItems.Add(p.IsHidden ? _loc.Get("Common.No") : _loc.Get("Common.Yes"));
            if (p.IsHidden) row.ForeColor = UiTheme.Palette.TextSecondary;
            _list.Items.Add(row);
            if (p.Id == select) row.Selected = true;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private void Add()
    {
        using var dialog = new DoseTimePresetDialog(null, null, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        var added = dialog.Result with { Id = Guid.NewGuid(), Order = _presets.Count };
        _presets.Add(added);
        Refill(added.Id);
    }

    private void Edit()
    {
        if (Selected is not { } current) return;
        using var dialog = new DoseTimePresetDialog(current, current.IsBuiltIn ? NameOf(current) : null, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _presets[_presets.IndexOf(current)] = dialog.Result;
        Refill(current.Id);
    }

    private void Remove()
    {
        if (Selected is not { IsBuiltIn: false } current) return;
        _presets.Remove(current);
        Refill();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var defaults = new Dictionary<int, IReadOnlyList<TimeOnly>>();
        foreach (var (n, box) in _defaultBoxes)
        {
            if (DoseTimeDefault.Parse(box.Text, n) is not { } times)
            {
                DialogLayout.ShowError(_error, _loc.Get("Ui.DoseTimesDialog.Defaults.Invalid", n), box);
                DialogResult = DialogResult.None;
                return;
            }
            defaults[n] = times;
        }
        Result = new DoseTimeSettings([.. _presets], defaults);
    }
}

// Editor of one preset. A built-in keeps its name and its as-needed
// default; only its time and visibility change.
internal sealed class DoseTimePresetDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly EffectiveDoseTimePreset? _seed;
    private readonly TextBox _labelBox;
    private readonly CheckBox _hasTime;
    private readonly DateTimePicker _timePicker;
    private readonly CheckBox _asNeeded;
    private readonly CheckBox _shown;
    private readonly Label _error = DialogLayout.ErrorLabel();

    public EffectiveDoseTimePreset? Result { get; private set; }

    public DoseTimePresetDialog(EffectiveDoseTimePreset? seed, string? builtInName, ILocalizationService localization)
    {
        _loc = localization;
        _seed = seed;
        Text = _loc.Get(seed is null ? "Ui.DoseTimePresetDialog.Title.New" : "Ui.DoseTimePresetDialog.Title.Edit");
        Width = 460;
        Height = 300;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        var builtIn = seed?.IsBuiltIn == true;
        _labelBox = new TextBox
        {
            Dock = DockStyle.Fill,
            MaxLength = 200,
            Text = builtIn ? builtInName ?? string.Empty : seed?.Label ?? string.Empty,
            ReadOnly = builtIn,
        };
        _hasTime = new CheckBox
        {
            Text = _loc.Get("Ui.DoseTimePresetDialog.HasTime"),
            AutoSize = true,
            Checked = seed is null || seed.Time is not null,
        };
        _timePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 100,
            Value = DateTime.Today.Add((seed?.Time ?? new TimeOnly(8, 0)).ToTimeSpan()),
            Enabled = _hasTime.Checked,
        };
        _hasTime.CheckedChanged += (_, _) => _timePicker.Enabled = _hasTime.Checked;
        _asNeeded = new CheckBox
        {
            Text = _loc.Get("Ui.DoseTimePresetDialog.AsNeeded"),
            AutoSize = true,
            Checked = seed?.IsAsNeeded ?? false,
            Enabled = !builtIn,
        };
        _shown = new CheckBox
        {
            Text = _loc.Get("Ui.DoseTimePresetDialog.Shown"),
            AutoSize = true,
            Checked = !(seed?.IsHidden ?? false),
        };

        var table = DialogLayout.FormTable();
        DialogLayout.AddRow(table, _loc.Get("Ui.DoseTimePresetDialog.Name"), _labelBox);
        DialogLayout.AddRow(table, _loc.Get("Ui.DoseTimePresetDialog.Time"), DialogLayout.Row(_hasTime, _timePicker));
        DialogLayout.AddRow(table, string.Empty, _asNeeded);
        DialogLayout.AddRow(table, string.Empty, _shown);
        DialogLayout.AddRow(table, string.Empty, _error);

        var okButton = DialogLayout.Button(_loc.Get("Common.Ok"), DialogResult.OK);
        var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
        okButton.Click += OnConfirm;
        var buttons = DialogLayout.ButtonBar(this, okButton, cancelButton);

        Controls.Add(table);
        Controls.Add(buttons);
    }

    private void OnConfirm(object? sender, EventArgs e)
    {
        var builtIn = _seed?.IsBuiltIn == true;
        if (!builtIn && string.IsNullOrWhiteSpace(_labelBox.Text))
        {
            DialogLayout.ShowError(_error, _loc.Get("Ui.DoseTimePresetDialog.Validation.Name"), _labelBox);
            DialogResult = DialogResult.None;
            return;
        }

        TimeOnly? time = _hasTime.Checked ? TimeOnly.FromDateTime(_timePicker.Value) : null;
        Result = _seed is { } seed
            ? seed with
            {
                Label = builtIn ? null : _labelBox.Text.Trim(),
                Time = time,
                IsAsNeeded = builtIn ? seed.IsAsNeeded : _asNeeded.Checked,
                IsHidden = !_shown.Checked,
            }
            : new EffectiveDoseTimePreset(Guid.Empty, null, _labelBox.Text.Trim(), time, _asNeeded.Checked, 0, !_shown.Checked);
    }
}
