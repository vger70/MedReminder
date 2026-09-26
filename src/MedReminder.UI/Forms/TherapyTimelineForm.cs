using MedReminder.Application.Abstractions;
using MedReminder.Application.Timeline;
using MedReminder.UI.Controls;

namespace MedReminder.UI.Forms;

// Read-only therapy timeline (EVOLUTION-PROPOSALS §4.3). Shows the
// chart, a legend, the estimate disclaimer and a details pane that
// carries the same information as text. "Show in list" closes the
// window with DialogResult.OK and SelectedMedicineId set, so MainForm
// can select that medicine and the existing actions apply to it.
//
// Data is loaded through the delegate supplied by MainForm, which opens
// a fresh DI scope per call; nothing is written.
internal sealed class TherapyTimelineForm : MedReminderFormBase
{
    private const int ButtonShiftDays = 30;

    private readonly ILocalizationService _loc;
    private readonly TherapyTimelineText _text;
    private readonly Func<TimelineWindow?, Task<TherapyTimeline>> _load;
    private readonly Guid? _initialSelection;

    private readonly TherapyTimelineChart _chart;
    private readonly Label _rangeLabel;
    private readonly TextBox _details;
    private readonly Label _disclaimer;
    private readonly Button _earlier;
    private readonly Button _today;
    private readonly Button _later;
    private readonly Button _showInList;

    private TimelineWindow? _window;
    private bool _loading;

    public TherapyTimelineForm(
        ILocalizationService localization,
        Func<TimelineWindow?, Task<TherapyTimeline>> load,
        Guid? initialSelection)
    {
        _loc = localization;
        _text = new TherapyTimelineText(localization);
        _load = load;
        _initialSelection = initialSelection;

        Text = _loc.Get("Ui.TherapyTimeline.Title");
        Font = new Font("Segoe UI", 9.75F);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;

        // Size from the font so the window grows with Windows scaling,
        // capped to the screen.
        var lh = Font.Height;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(lh * 62, area.Width - lh * 2), Math.Min(lh * 40, area.Height - lh * 2));
        MinimumSize = new Size(Math.Min(lh * 40, area.Width), Math.Min(lh * 28, area.Height));

        _earlier = new Button { Text = _loc.Get("Ui.TherapyTimeline.Nav.Earlier"), AutoSize = true };
        _today = new Button { Text = _loc.Get("Ui.TherapyTimeline.Nav.Today"), AutoSize = true };
        _later = new Button { Text = _loc.Get("Ui.TherapyTimeline.Nav.Later"), AutoSize = true };
        _rangeLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(lh / 2, lh / 3, 0, 0),
            UseMnemonic = false,
        };
        _earlier.Click += async (_, _) => await ShiftAsync(-ButtonShiftDays);
        _later.Click += async (_, _) => await ShiftAsync(ButtonShiftDays);
        _today.Click += async (_, _) => await LoadAsync(null);

        var nav = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0),
        };
        nav.Controls.Add(_earlier);
        nav.Controls.Add(_today);
        nav.Controls.Add(_later);
        nav.Controls.Add(_rangeLabel);

        _chart = new TherapyTimelineChart
        {
            Dock = DockStyle.Fill,
            AccessibleName = _loc.Get("Ui.TherapyTimeline.Chart.AccessibleName"),
            Margin = new Padding(0, lh / 3, 0, lh / 3),
        };
        _chart.SelectedIndexChanged += (_, _) => UpdateDetails();
        _chart.RowActivated += (_, _) => ShowInList();
        _chart.WindowShiftRequested += async (_, days) => await ShiftAsync(days);

        var legend = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0),
        };
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.Active, _loc.Get("Ui.TherapyTimeline.Legend.Active")));
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.Suspended, _loc.Get("Ui.TherapyTimeline.Legend.Suspended")));
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.ScheduleChange, _loc.Get("Ui.TherapyTimeline.Legend.ScheduleChange")));
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.TaperStage, _loc.Get("Ui.TherapyTimeline.Legend.TaperStage")));
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.RunOut, _loc.Get("Ui.TherapyTimeline.Legend.RunOut")));
        legend.Controls.Add(new TimelineLegendEntry(TimelineLegendEntry.Symbol.Today, _loc.Get("Ui.TherapyTimeline.Legend.Today")));
        foreach (Control entry in legend.Controls)
        {
            entry.Margin = new Padding(0, 0, lh, lh / 4);
        }

        _disclaimer = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.TherapyTimeline.Disclaimer"),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, lh / 4, 0, lh / 4),
            UseMnemonic = false,
        };

        // The label's mnemonic moves focus to the details box, the next
        // control in tab order.
        var detailsLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.TherapyTimeline.Details.Label"),
            Margin = new Padding(0, lh / 4, 0, 0),
        };
        _details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            AccessibleName = detailsLabel.Text.Replace("&", string.Empty, StringComparison.Ordinal),
        };

        _showInList = new Button
        {
            Text = _loc.Get("Ui.TherapyTimeline.ShowInList"),
            AutoSize = true,
            Enabled = false,
        };
        _showInList.Click += (_, _) => ShowInList();
        var close = new Button
        {
            Text = _loc.Get("Ui.TherapyTimeline.Close"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, lh / 3, 0, 0),
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(_showInList);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(lh / 2),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // navigation
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));       // chart
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // legend
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // disclaimer
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // details label
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, lh * 9));   // details
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));           // buttons
        layout.Controls.Add(nav, 0, 0);
        layout.Controls.Add(_chart, 0, 1);
        layout.Controls.Add(legend, 0, 2);
        layout.Controls.Add(_disclaimer, 0, 3);
        layout.Controls.Add(detailsLabel, 0, 4);
        layout.Controls.Add(_details, 0, 5);
        layout.Controls.Add(buttons, 0, 6);
        Controls.Add(layout);

        // Tab order: navigation, chart, details, buttons.
        nav.TabIndex = 0;
        _chart.TabIndex = 1;
        legend.TabIndex = 2;
        detailsLabel.TabIndex = 3;
        _details.TabIndex = 4;
        buttons.TabIndex = 5;

        CancelButton = close;
        ActiveControl = _chart;

        Resize += (_, _) => WrapDisclaimer();
        Shown += async (_, _) =>
        {
            WrapDisclaimer();
            await LoadAsync(null);
        };
        UpdateDetails();
    }

    // Medicine chosen with "Show in list"; valid when DialogResult is OK.
    public Guid? SelectedMedicineId { get; private set; }

    private void WrapDisclaimer()
        => _disclaimer.MaximumSize = new Size(Math.Max(Font.Height * 10, ClientSize.Width - Font.Height * 2), 0);

    private Task ShiftAsync(int days)
        => _window is { } w ? LoadAsync(w.Shift(days)) : Task.CompletedTask;

    private async Task LoadAsync(TimelineWindow? window)
    {
        if (_loading) return;
        _loading = true;
        UseWaitCursor = true;
        try
        {
            var selected = _chart.SelectedRow?.MedicineId ?? _initialSelection;
            var timeline = await _load(window);
            if (IsDisposed) return;

            _window = timeline.Window;
            _chart.SetContent(timeline, _text,
                _loc.Get("Ui.TherapyTimeline.Today"),
                _loc.Get("Ui.TherapyTimeline.Chart.Empty"),
                selected);
            _rangeLabel.Text = _loc.Get("Ui.TherapyTimeline.Range",
                _text.Date(timeline.Window.Start), _text.Date(timeline.Window.End));
            UpdateDetails();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            MessageBox.Show(this,
                _loc.Get("Ui.TherapyTimeline.LoadError") + Environment.NewLine + Environment.NewLine + ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _loading = false;
            if (!IsDisposed) UseWaitCursor = false;
        }
    }

    private void UpdateDetails()
    {
        var row = _chart.SelectedRow;
        _showInList.Enabled = row is not null;
        _details.Text = row is null
            ? _loc.Get("Ui.TherapyTimeline.Details.Hint")
            : _text.DescribeRow(row);
    }

    private void ShowInList()
    {
        if (_chart.SelectedRow is not { } row) return;
        SelectedMedicineId = row.MedicineId;
        DialogResult = DialogResult.OK;
    }
}
