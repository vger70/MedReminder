using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Ledger;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.UI.Forms;

// History of the facts entered for one medicine (stock entries,
// intakes, stock counts, suspensions), newest first, with Delete on the
// entries that can still be retracted (B.1 Phase 2d, D8). The data and
// the retraction come from the caller (FactHistoryQuery / RetractFact),
// so the dialog holds no Application logic.
internal sealed class FactHistoryDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly string _unit;
    private readonly Func<Task<IReadOnlyList<FactHistoryItem>>> _load;
    private readonly Func<FactHistoryItem, Task> _retract;
    private readonly ListView _list;
    private readonly Button _deleteButton;

    public FactHistoryDialog(
        string medicineName,
        string unit,
        Func<Task<IReadOnlyList<FactHistoryItem>>> load,
        Func<FactHistoryItem, Task> retract,
        ILocalizationService localization)
    {
        _loc = localization;
        _unit = unit;
        _load = load;
        _retract = retract;

        Text = _loc.Get("Ui.FactHistoryDialog.Title", medicineName);
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 48,
            Padding = new Padding(12, 8, 12, 0),
            Text = _loc.Get("Ui.FactHistoryDialog.Hint"),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Recorded"), 130);
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Day"), 90);
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Type"), 150);
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Detail"), 220);
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Notes"), 140);
        _list.Columns.Add(_loc.Get("Ui.FactHistoryDialog.Column.Deletion"), 170);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        _deleteButton = new Button
        {
            Text = _loc.Get("Ui.FactHistoryDialog.Delete"),
            AutoSize = true,
            Height = 32,
            Enabled = false,
        };
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        var closeButton = new Button
        {
            Text = _loc.Get("Common.Close"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Height = 32,
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_deleteButton);

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);
        CancelButton = closeButton;

        Shown += async (_, _) => await ReloadAsync();
    }

    // True when at least one fact was retracted: the caller reloads.
    public bool Changed { get; private set; }

    private FactHistoryItem? Selected
        => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as FactHistoryItem : null;

    private void UpdateButtons() => _deleteButton.Enabled = Selected is { CanRetract: true };

    private async Task ReloadAsync()
    {
        IReadOnlyList<FactHistoryItem> items;
        try
        {
            items = await _load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get("Ui.MainForm.Error.FactHistory"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in items)
        {
            var row = new ListViewItem(FormatRecorded(item.RecordedAt)) { Tag = item };
            row.SubItems.Add(item.Day.ToString("d", CultureInfo.CurrentCulture));
            row.SubItems.Add(KindText(item));
            row.SubItems.Add(DetailText(item));
            row.SubItems.Add(item.Notes ?? string.Empty);
            row.SubItems.Add(DeletionText(item.Block));
            if (!item.CanRetract) row.ForeColor = SystemColors.GrayText;
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    private async Task DeleteSelectedAsync()
    {
        if (Selected is not { CanRetract: true } item) return;

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.FactHistoryDialog.Confirm"),
            _loc.Get("Ui.FactHistoryDialog.Confirm.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            _deleteButton.Enabled = false;
            await _retract(item);
            Changed = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, _loc.Get("Ui.FactHistoryDialog.Error.Delete"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        await ReloadAsync();
    }

    // Facts recorded before the recording instant existed read back as
    // MinValue.
    private string FormatRecorded(DateTimeOffset recordedAt)
        => recordedAt == DateTimeOffset.MinValue
            ? _loc.Get("Common.NotAvailable")
            : recordedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private string KindText(FactHistoryItem item) => item.Kind switch
    {
        FactKind.StockEntry => item.MovementKind switch
        {
            StockMovementKind.InitialLoad => _loc.Get("Ui.FactHistoryDialog.Kind.InitialLoad"),
            { } kind => _loc.Get($"Ui.StockAdjustmentDialog.Kind.{kind}"),
            null => string.Empty,
        },
        FactKind.Intake => _loc.Get("Ui.FactHistoryDialog.Kind.Intake"),
        FactKind.StockCount => _loc.Get("Ui.FactHistoryDialog.Kind.StockCount"),
        FactKind.Suspension => _loc.Get("Ui.FactHistoryDialog.Kind.Suspension"),
        _ => item.Kind.ToString(),
    };

    private string DetailText(FactHistoryItem item) => item.Kind switch
    {
        FactKind.StockEntry => _loc.Get("Ui.FactHistoryDialog.Detail.Quantity", Signed(item.Quantity), _unit),
        FactKind.Intake => _loc.Get("Ui.FactHistoryDialog.Detail.Intake",
            StatusText(item.IntakeStatus), Number(item.Quantity), _unit),
        FactKind.StockCount => _loc.Get("Ui.FactHistoryDialog.Detail.Count",
            Number(item.Quantity), _unit, Signed(item.Correction)),
        FactKind.Suspension => item.EndDate is { } end
            ? _loc.Get("Ui.FactHistoryDialog.Detail.SuspensionClosed",
                item.Day.ToString("d", CultureInfo.CurrentCulture), end.ToString("d", CultureInfo.CurrentCulture))
            : _loc.Get("Ui.FactHistoryDialog.Detail.SuspensionOpen",
                item.Day.ToString("d", CultureInfo.CurrentCulture)),
        _ => string.Empty,
    };

    private string StatusText(IntakeStatus? status) => status switch
    {
        IntakeStatus.ManualCorrection => _loc.Get("Ui.FactHistoryDialog.Status.ManualCorrection"),
        { } s => _loc.Get($"Ui.IntakeDialog.Status.{s}"),
        null => string.Empty,
    };

    private string DeletionText(RetractionBlock block) => block switch
    {
        RetractionBlock.None => _loc.Get("Ui.FactHistoryDialog.Deletion.Allowed"),
        RetractionBlock.Legacy => _loc.Get("Ui.FactHistoryDialog.Deletion.Legacy"),
        RetractionBlock.LaterCount => _loc.Get("Ui.FactHistoryDialog.Deletion.LaterCount"),
        _ => block.ToString(),
    };

    private static string Number(decimal? value)
        => value?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string Signed(decimal? value)
        => value is { } v ? v.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) : string.Empty;
}
