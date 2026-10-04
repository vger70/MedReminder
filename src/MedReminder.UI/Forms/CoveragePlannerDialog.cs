using MedReminder.Application.Abstractions;
using MedReminder.Application.Coverage;
using MedReminder.Application.Reporting;
using MedReminder.UI.Printing;
using MedReminder.UI.UiExtensions;

namespace MedReminder.UI.Forms;

// Therapy → Plan supply (docs/notes/EVOLUTION-PROPOSALS-2.md §3.5): the
// user picks a period (a trip, the days until the next pharmacy visit)
// and sees, for every active medicine, how much the period needs, how
// much will be left when it starts and how much is missing, with the
// packages to get. Read-only: the plan comes from the caller
// (CoveragePlanQuery) and nothing is written. Copy, print and PDF reuse
// the therapy report's behaviour (PrintOutput).
internal sealed class CoveragePlannerDialog : MedReminderFormBase
{
    // Default period: the next two weeks, today included.
    private const int DefaultDays = 14;

    private readonly ILocalizationService _loc;
    private readonly Func<DateOnly, DateOnly, Task<CoveragePlan>> _load;
    private readonly string? _profileName;
    private readonly DateTimePicker _from;
    private readonly DateTimePicker _to;
    private readonly Label _error;
    private readonly ListView _list;
    private readonly Label _summary;
    private readonly Button _copyButton;
    private readonly Button _pdfButton;
    private readonly Button _printButton;
    private CoveragePlan? _plan;
    private int _loadVersion;

    public CoveragePlannerDialog(
        Func<DateOnly, DateOnly, Task<CoveragePlan>> load,
        DateOnly today,
        string? profileName,
        ILocalizationService localization)
    {
        _loc = localization;
        _load = load;
        _profileName = profileName;

        Text = _loc.Get("Ui.CoveragePlannerDialog.Title");
        Width = 860;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 56,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
            Text = _loc.Get("Ui.CoveragePlannerDialog.Hint"),
        };

        var start = today.ToDateTime(TimeOnly.MinValue);
        _from = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Width = 130,
            MinDate = start,
            Value = start,
        };
        _to = new DateTimePicker
        {
            Format = DateTimePickerFormat.Short,
            Width = 130,
            MinDate = start,
            Value = start.AddDays(DefaultDays - 1),
        };
        _from.ValueChanged += async (_, _) => await ReloadAsync();
        _to.ValueChanged += async (_, _) => await ReloadAsync();

        var fromLabel = new Label { Text = _loc.Get("Ui.CoveragePlannerDialog.From"), AutoSize = true };
        var toLabel = new Label { Text = _loc.Get("Ui.CoveragePlannerDialog.To"), AutoSize = true };
        _error = DialogLayout.ErrorLabel();
        _error.MaximumSize = new Size(0, 0);

        var periodRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, UiTheme.Space.S),
        };
        foreach (var label in new[] { fromLabel, toLabel })
        {
            label.Margin = new Padding(0, UiTheme.Space.S, UiTheme.Space.S, 0);
        }
        _from.Margin = new Padding(0, 0, UiTheme.Space.L, 0);
        _to.Margin = new Padding(0, 0, UiTheme.Space.L, 0);
        _error.Margin = new Padding(0, UiTheme.Space.S, 0, 0);
        periodRow.Controls.Add(fromLabel);
        periodRow.Controls.Add(_from);
        periodRow.Controls.Add(toLabel);
        periodRow.Controls.Add(_to);
        periodRow.Controls.Add(_error);

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Reports.Coverage.Column.Medicine"), 230);
        _list.Columns.Add(_loc.Get("Reports.Coverage.Column.Needed"), 140);
        _list.Columns.Add(_loc.Get("Reports.Coverage.Column.StockAtStart"), 140);
        _list.Columns.Add(_loc.Get("Reports.Coverage.Column.Missing"), 130);
        _list.Columns.Add(_loc.Get("Reports.Coverage.Column.Packages"), 160);

        _summary = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 32,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, 0),
        };

        _copyButton = DialogLayout.Button(_loc.Get("Ui.TherapyReportDialog.Copy"));
        _pdfButton = DialogLayout.Button(_loc.Get("Ui.TherapyReportDialog.SavePdf"));
        _printButton = DialogLayout.Button(_loc.Get("Ui.TherapyReportDialog.Print"));
        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        _copyButton.Click += (_, _) => Copy();
        _pdfButton.Click += (_, _) => SaveAsPdf();
        _printButton.Click += (_, _) => Print();
        var buttons = DialogLayout.ButtonBar(this, closeButton, closeButton, _printButton, _pdfButton, _copyButton);

        Controls.Add(_list);
        Controls.Add(periodRow);
        Controls.Add(hint);
        Controls.Add(_summary);
        Controls.Add(buttons);
        DialogLayout.KeepButtonsVisible(this, buttons);

        UpdateButtons();
        Shown += async (_, _) => await ReloadAsync();
    }

    private DateOnly From => DateOnly.FromDateTime(_from.Value);

    private DateOnly To => DateOnly.FromDateTime(_to.Value);

    private void UpdateButtons()
    {
        var ready = _plan is not null;
        _copyButton.Enabled = ready;
        _pdfButton.Enabled = ready;
        _printButton.Enabled = ready;
    }

    private async Task ReloadAsync()
    {
        var from = From;
        var to = To;
        string? error = null;
        if (to < from)
        {
            error = _loc.Get("Ui.CoveragePlannerDialog.Error.Order");
        }
        else if (to.DayNumber - from.DayNumber + 1 > CoveragePlanner.MaxDays)
        {
            error = _loc.Get("Ui.CoveragePlannerDialog.Error.TooLong", CoveragePlanner.MaxDays);
        }
        DialogLayout.ShowError(_error, error);
        if (error is not null)
        {
            Display(null);
            return;
        }

        // A later change of the dates supersedes a load still running.
        var version = ++_loadVersion;
        CoveragePlan plan;
        try
        {
            plan = await _load(from, to);
        }
        catch (Exception ex)
        {
            if (version != _loadVersion) return;
            Display(null);
            UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.CoveragePlannerDialog.Error.Load"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (version != _loadVersion || IsDisposed) return;
        Display(plan);
    }

    private void Display(CoveragePlan? plan)
    {
        _plan = plan;
        _list.BeginUpdate();
        _list.Items.Clear();
        if (plan is not null)
        {
            var c = _loc.CurrentCulture;
            foreach (var row in plan.Rows)
            {
                var name = string.IsNullOrWhiteSpace(row.ActiveIngredient)
                    ? row.Name
                    : $"{row.Name} ({row.ActiveIngredient})";
                var item = new ListViewItem(name) { Tag = row };
                item.SubItems.Add(CoveragePlanText.Needed(row, _loc, c));
                item.SubItems.Add(CoveragePlanText.StockAtStart(row, _loc, c));
                item.SubItems.Add(CoveragePlanText.Missing(row, _loc, c));
                item.SubItems.Add(CoveragePlanText.Packages(row, _loc, c));
                if (row.Status == CoverageStatus.Short)
                {
                    item.ForeColor = UiTheme.Palette.DangerText;
                }
                else if (row.Status is CoverageStatus.AsNeeded or CoverageStatus.NotInUse)
                {
                    item.ForeColor = UiTheme.Palette.TextSecondary;
                }
                _list.Items.Add(item);
            }
        }
        _list.EndUpdate();
        _summary.Text = plan is null ? string.Empty : CoveragePlanText.Summary(plan, _loc);
        UpdateButtons();
    }

    private PrintableTable Table() => CoveragePlanText.BuildTable(_plan!, _profileName, _loc);

    private string FileName(string extension) => $"MedReminder-supply-{From:yyyyMMdd}-{To:yyyyMMdd}.{extension}";

    private void Copy()
    {
        if (_plan is null) return;
        PrintOutput.CopyText(this, PrintableTableText.Render(Table()), _loc);
    }

    private TablePrintDocument CreatePrintDocument()
        => new(Table(), PrintOutput.DefaultPaper(), _loc.Get("Reports.Therapy.Page"), "MedReminder — Supply plan");

    private void SaveAsPdf()
    {
        if (_plan is null) return;
        PrintOutput.SaveAsPdf(this, CreatePrintDocument, _loc.Get("Ui.CoveragePlannerDialog.PdfDialog.Title"),
            FileName("pdf"), _loc);
    }

    private void Print()
    {
        if (_plan is null) return;
        PrintOutput.Preview(this, CreatePrintDocument, _loc);
    }
}
