using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.UI.Controls;

// Simple / Advanced schedule editor shared by MedicineEditDialog
// (create flow) and ChangeScheduleDialog (mid-therapy change).
//
// - Simple mode: the caller keeps its own dose / administrations
//   inputs; TryBuildSchedule returns null and the caller falls back
//   to its legacy scalar plumbing.
// - Advanced mode: the panel owns a Regime-type dropdown and one
//   sub-panel per ScheduleKind; TryBuildSchedule returns the value
//   object corresponding to the current inputs.
//
// The Root control is intended to be embedded inside a
// TableLayoutPanel row via AddRow. It docks to Fill so the caller
// controls the width; height auto-grows with the visible sub-panel.
//
// See docs/ANALYSIS-A1-REGIMENS.md §5.
internal sealed class SchedulePanel
{
    private readonly ILocalizationService _loc;
    private readonly RadioButton _modeSimple;
    private readonly RadioButton _modeAdvanced;
    private readonly ComboBox _kindBox;
    private readonly Panel _advancedContainer;
    private readonly Panel _fixedPanel;
    private readonly Panel _weeklyPanel;
    private readonly Panel _cyclicPanel;
    private readonly Panel _taperingPanel;
    private readonly Panel _prnPanel;

    // FixedDaily controls.
    private readonly NumericUpDown _fixedDose;
    private readonly NumericUpDown _fixedAdmin;

    // Weekly controls.
    private readonly NumericUpDown[] _weeklyQuantities;

    // Cyclic controls.
    private readonly NumericUpDown _cyclicOn;
    private readonly NumericUpDown _cyclicOff;
    private readonly NumericUpDown _cyclicQuantity;
    private readonly Label _cyclicSummary;

    // Tapering controls.
    private readonly NumericUpDown _taperingStart;
    private readonly NumericUpDown _taperingEnd;
    private readonly NumericUpDown _taperingStep;
    private readonly NumericUpDown _taperingInterval;
    private readonly Label _taperingSummary;

    public SchedulePanel(ILocalizationService localization)
    {
        _loc = localization;

        _modeSimple = new RadioButton
        {
            Text = _loc.Get("Ui.Schedule.Mode.Simple"),
            AutoSize = true,
            Checked = true,
        };
        _modeAdvanced = new RadioButton
        {
            Text = _loc.Get("Ui.Schedule.Mode.Advanced"),
            AutoSize = true,
        };
        _modeSimple.CheckedChanged += (_, _) => OnModeChanged();
        _modeAdvanced.CheckedChanged += (_, _) => OnModeChanged();

        _kindBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240,
        };
        _kindBox.Items.Add(new KindItem(ScheduleKind.FixedDaily, _loc.Get("Ui.Schedule.Kind.FixedDaily")));
        _kindBox.Items.Add(new KindItem(ScheduleKind.Weekly, _loc.Get("Ui.Schedule.Kind.Weekly")));
        _kindBox.Items.Add(new KindItem(ScheduleKind.Cyclic, _loc.Get("Ui.Schedule.Kind.Cyclic")));
        _kindBox.Items.Add(new KindItem(ScheduleKind.Tapering, _loc.Get("Ui.Schedule.Kind.Tapering")));
        _kindBox.Items.Add(new KindItem(ScheduleKind.Prn, _loc.Get("Ui.Schedule.Kind.Prn")));
        _kindBox.SelectedIndex = 0;
        _kindBox.SelectedIndexChanged += (_, _) => OnKindChanged();

        _fixedDose = MakeDecimal(0.01m, 1000m, decimals: 2, initial: 1m);
        _fixedAdmin = MakeInteger(1, 24, initial: 1);
        _fixedPanel = BuildFixedPanel();

        _weeklyQuantities = new NumericUpDown[7];
        for (var i = 0; i < 7; i++) _weeklyQuantities[i] = MakeDecimal(0m, 1000m, decimals: 2, initial: 0m);
        _weeklyPanel = BuildWeeklyPanel();

        _cyclicOn = MakeInteger(1, 365, initial: 21);
        _cyclicOff = MakeInteger(0, 365, initial: 7);
        _cyclicQuantity = MakeDecimal(0.01m, 1000m, decimals: 2, initial: 1m);
        _cyclicSummary = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
        };
        _cyclicOn.ValueChanged += (_, _) => UpdateCyclicSummary();
        _cyclicOff.ValueChanged += (_, _) => UpdateCyclicSummary();
        _cyclicQuantity.ValueChanged += (_, _) => UpdateCyclicSummary();
        _cyclicPanel = BuildCyclicPanel();
        UpdateCyclicSummary();

        _taperingStart = MakeDecimal(0.01m, 1000m, decimals: 2, initial: 4m);
        _taperingEnd = MakeDecimal(0m, 1000m, decimals: 2, initial: 0.5m);
        _taperingStep = MakeDecimal(0.01m, 1000m, decimals: 2, initial: 0.5m);
        _taperingInterval = MakeInteger(1, 365, initial: 7);
        _taperingSummary = new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.Color.DarkGray,
        };
        _taperingStart.ValueChanged += (_, _) => UpdateTaperingSummary();
        _taperingEnd.ValueChanged += (_, _) => UpdateTaperingSummary();
        _taperingStep.ValueChanged += (_, _) => UpdateTaperingSummary();
        _taperingInterval.ValueChanged += (_, _) => UpdateTaperingSummary();
        _taperingPanel = BuildTaperingPanel();
        UpdateTaperingSummary();

        _prnPanel = BuildPrnPanel();

        _advancedContainer = BuildAdvancedContainer();
        Root = BuildRoot();

        // Initial visibility state — Simple selected, Advanced hidden.
        _advancedContainer.Visible = false;
        UpdateSubPanelsVisibility();
    }

    public Control Root { get; }

    // True when the user has selected Advanced mode.
    public bool AdvancedSelected => _modeAdvanced.Checked;

    // Raised when the user flips Simple ↔ Advanced. Callers can use
    // this to disable outer Dose / Admin / Slots controls whose
    // values become display-only under Advanced.
    public event EventHandler? ModeChanged;

    // Builds the Schedule value object corresponding to the current
    // Advanced-mode inputs. Returns null in Simple mode (the caller
    // should fall back to its own Dose / AdministrationsPerDay
    // fields). Populates `error` with a localized message when a
    // validation invariant is violated; returns null in that case
    // too.
    public Schedule? TryBuildSchedule(out string? error)
    {
        error = null;
        if (!_modeAdvanced.Checked)
        {
            return null;
        }
        var kind = SelectedKind();
        try
        {
            return kind switch
            {
                ScheduleKind.FixedDaily => new FixedDailySchedule(_fixedDose.Value, (int)_fixedAdmin.Value),
                ScheduleKind.Weekly => BuildWeekly(),
                ScheduleKind.Cyclic => new CyclicSchedule((int)_cyclicOn.Value, (int)_cyclicOff.Value, _cyclicQuantity.Value),
                ScheduleKind.Tapering => new TaperingSchedule(
                    _taperingStart.Value, _taperingEnd.Value, _taperingStep.Value, (int)_taperingInterval.Value),
                ScheduleKind.Prn => new PrnSchedule(),
                _ => new FixedDailySchedule(_fixedDose.Value, (int)_fixedAdmin.Value),
            };
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    // Seeds the panel from an existing Schedule (Edit / Change flow).
    // null → default to Simple mode.
    public void ApplySchedule(Schedule? seed)
    {
        if (seed is null || seed is FixedDailySchedule)
        {
            _modeSimple.Checked = true;
            if (seed is FixedDailySchedule f)
            {
                _fixedDose.Value = ClampDecimal(f.DosePerAdministration, _fixedDose.Minimum, _fixedDose.Maximum);
                _fixedAdmin.Value = ClampInt(f.AdministrationsPerDay, (int)_fixedAdmin.Minimum, (int)_fixedAdmin.Maximum);
            }
            return;
        }

        _modeAdvanced.Checked = true;
        switch (seed)
        {
            case WeeklySchedule w:
                SelectKind(ScheduleKind.Weekly);
                for (var i = 0; i < 7; i++)
                {
                    _weeklyQuantities[i].Value = ClampDecimal(
                        w.QuantitiesByDayOfWeek[i], _weeklyQuantities[i].Minimum, _weeklyQuantities[i].Maximum);
                }
                break;
            case CyclicSchedule c:
                SelectKind(ScheduleKind.Cyclic);
                _cyclicOn.Value = ClampInt(c.OnDays, (int)_cyclicOn.Minimum, (int)_cyclicOn.Maximum);
                _cyclicOff.Value = ClampInt(c.OffDays, (int)_cyclicOff.Minimum, (int)_cyclicOff.Maximum);
                _cyclicQuantity.Value = ClampDecimal(c.QuantityPerOnDay, _cyclicQuantity.Minimum, _cyclicQuantity.Maximum);
                UpdateCyclicSummary();
                break;
            case TaperingSchedule t:
                SelectKind(ScheduleKind.Tapering);
                _taperingStart.Value = ClampDecimal(t.StartDose, _taperingStart.Minimum, _taperingStart.Maximum);
                _taperingEnd.Value = ClampDecimal(t.EndDose, _taperingEnd.Minimum, _taperingEnd.Maximum);
                _taperingStep.Value = ClampDecimal(t.Step, _taperingStep.Minimum, _taperingStep.Maximum);
                _taperingInterval.Value = ClampInt(t.IntervalDays, (int)_taperingInterval.Minimum, (int)_taperingInterval.Maximum);
                UpdateTaperingSummary();
                break;
            case PrnSchedule:
                SelectKind(ScheduleKind.Prn);
                break;
        }
    }

    private ScheduleKind SelectedKind()
    {
        return _kindBox.SelectedItem is KindItem item
            ? item.Kind
            : ScheduleKind.FixedDaily;
    }

    private void SelectKind(ScheduleKind kind)
    {
        for (var i = 0; i < _kindBox.Items.Count; i++)
        {
            if (_kindBox.Items[i] is KindItem item && item.Kind == kind)
            {
                _kindBox.SelectedIndex = i;
                return;
            }
        }
    }

    private WeeklySchedule BuildWeekly()
    {
        var days = new decimal[7];
        for (var i = 0; i < 7; i++) days[i] = _weeklyQuantities[i].Value;
        return new WeeklySchedule(days);
    }

    private void OnModeChanged()
    {
        _advancedContainer.Visible = _modeAdvanced.Checked;
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnKindChanged()
    {
        UpdateSubPanelsVisibility();
    }

    private void UpdateSubPanelsVisibility()
    {
        var kind = SelectedKind();
        _fixedPanel.Visible = kind == ScheduleKind.FixedDaily;
        _weeklyPanel.Visible = kind == ScheduleKind.Weekly;
        _cyclicPanel.Visible = kind == ScheduleKind.Cyclic;
        _taperingPanel.Visible = kind == ScheduleKind.Tapering;
        _prnPanel.Visible = kind == ScheduleKind.Prn;
    }

    private void UpdateCyclicSummary()
    {
        _cyclicSummary.Text = string.Format(
            CultureInfo.CurrentUICulture,
            _loc.Get("Ui.Schedule.Cyclic.Summary"),
            _cyclicOn.Value.ToString("0", CultureInfo.CurrentUICulture),
            _cyclicOff.Value.ToString("0", CultureInfo.CurrentUICulture),
            _cyclicQuantity.Value.ToString("0.##", CultureInfo.CurrentUICulture));
    }

    private void UpdateTaperingSummary()
    {
        _taperingSummary.Text = string.Format(
            CultureInfo.CurrentUICulture,
            _loc.Get("Ui.Schedule.Tapering.Summary"),
            _taperingStart.Value.ToString("0.##", CultureInfo.CurrentUICulture),
            _taperingEnd.Value.ToString("0.##", CultureInfo.CurrentUICulture),
            _taperingStep.Value.ToString("0.##", CultureInfo.CurrentUICulture),
            _taperingInterval.Value.ToString("0", CultureInfo.CurrentUICulture));
    }

    private Control BuildRoot()
    {
        // TableLayoutPanel with AutoSize rows is the only WinForms
        // container that reliably reports a correct AutoSize when it
        // is itself hosted inside an AutoSize cell of another
        // TableLayoutPanel (Panel + Dock=Fill collapses the outer row
        // to zero height; FlowLayoutPanel's AutoSize does not always
        // pick up its children's AutoSize until the first layout
        // pass). See docs/ANALYSIS-A1-REGIMENS.md §5.1.
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var modeRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            MinimumSize = new System.Drawing.Size(0, 26),
        };
        modeRow.Controls.Add(_modeSimple);
        modeRow.Controls.Add(_modeAdvanced);

        root.RowCount = 2;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(modeRow, 0, 0);
        root.Controls.Add(_advancedContainer, 0, 1);
        return root;
    }

    private TableLayoutPanel BuildAdvancedContainer()
    {
        var container = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 0),
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var kindRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        var kindLabel = new Label
        {
            Text = _loc.Get("Ui.Schedule.KindLabel"),
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0),
        };
        kindRow.Controls.Add(kindLabel);
        kindRow.Controls.Add(_kindBox);

        var subPanelsHost = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 6, 0, 0),
        };
        subPanelsHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        subPanelsHost.RowCount = 5;
        for (var i = 0; i < 5; i++) subPanelsHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        subPanelsHost.Controls.Add(_fixedPanel, 0, 0);
        subPanelsHost.Controls.Add(_weeklyPanel, 0, 1);
        subPanelsHost.Controls.Add(_cyclicPanel, 0, 2);
        subPanelsHost.Controls.Add(_taperingPanel, 0, 3);
        subPanelsHost.Controls.Add(_prnPanel, 0, 4);

        container.RowCount = 2;
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.Controls.Add(kindRow, 0, 0);
        container.Controls.Add(subPanelsHost, 0, 1);
        return container;
    }

    private Panel BuildFixedPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(panel, _loc.Get("Ui.Schedule.FixedDaily.Dose"), _fixedDose);
        AddRow(panel, _loc.Get("Ui.Schedule.FixedDaily.Administrations"), _fixedAdmin);
        return panel;
    }

    private Panel BuildWeeklyPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 7,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        string[] headers =
        {
            _loc.Get("Ui.Schedule.Weekly.Header.Mon"),
            _loc.Get("Ui.Schedule.Weekly.Header.Tue"),
            _loc.Get("Ui.Schedule.Weekly.Header.Wed"),
            _loc.Get("Ui.Schedule.Weekly.Header.Thu"),
            _loc.Get("Ui.Schedule.Weekly.Header.Fri"),
            _loc.Get("Ui.Schedule.Weekly.Header.Sat"),
            _loc.Get("Ui.Schedule.Weekly.Header.Sun"),
        };
        for (var col = 0; col < 7; col++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label
            {
                Text = headers[col],
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(4, 4, 4, 2),
            }, col, 0);
            panel.Controls.Add(_weeklyQuantities[col], col, 1);
        }
        return panel;
    }

    private Panel BuildCyclicPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(panel, _loc.Get("Ui.Schedule.Cyclic.OnDays"), _cyclicOn);
        AddRow(panel, _loc.Get("Ui.Schedule.Cyclic.OffDays"), _cyclicOff);
        AddRow(panel, _loc.Get("Ui.Schedule.Cyclic.QuantityPerOnDay"), _cyclicQuantity);
        AddRow(panel, string.Empty, _cyclicSummary);
        return panel;
    }

    private Panel BuildTaperingPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(panel, _loc.Get("Ui.Schedule.Tapering.StartDose"), _taperingStart);
        AddRow(panel, _loc.Get("Ui.Schedule.Tapering.EndDose"), _taperingEnd);
        AddRow(panel, _loc.Get("Ui.Schedule.Tapering.Step"), _taperingStep);
        AddRow(panel, _loc.Get("Ui.Schedule.Tapering.IntervalDays"), _taperingInterval);
        AddRow(panel, string.Empty, _taperingSummary);
        return panel;
    }

    private Panel BuildPrnPanel()
    {
        var panel = new Panel
        {
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 4),
        };
        panel.Controls.Add(new Label
        {
            Text = _loc.Get("Ui.Schedule.Prn.Help"),
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            ForeColor = System.Drawing.Color.DarkGray,
        });
        return panel;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 2) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }

    private static NumericUpDown MakeDecimal(decimal min, decimal max, int decimals, decimal initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = 0.5m,
            Value = initial,
            Width = 120,
        };

    private static NumericUpDown MakeInteger(int min, int max, int initial)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = 0,
            Increment = 1,
            Value = initial,
            Width = 120,
        };

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        => value < min ? min : (value > max ? max : value);

    private static int ClampInt(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    private sealed record KindItem(ScheduleKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
