using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;

namespace MedReminder.UI.Forms;

// Short list from which the user picks one medicine: several medicines
// carry the scanned code, or the user links the code to a medicine
// that has none (restock by scan, docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5C.3).
internal sealed class MedicinePickerDialog : MedReminderFormBase
{
    public RestockCandidate? Result { get; private set; }

    private readonly ListBox _list;
    private readonly Button _okButton;

    public MedicinePickerDialog(
        string title,
        string prompt,
        IReadOnlyList<RestockCandidate> candidates,
        ILocalizationService localization)
    {
        Text = title;
        ClientSize = new Size(460, 360);
        MinimumSize = new Size(360, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.75F);

        var inactiveSuffix = localization.Get("Ui.MedicinePickerDialog.InactiveSuffix");
        var promptLabel = new Label
        {
            Text = prompt,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 4, 4, 8),
        };
        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var candidate in candidates)
        {
            _list.Items.Add(new Item(candidate, candidate.IsActive ? candidate.Name : candidate.Name + " " + inactiveSuffix));
        }
        _list.SelectedIndexChanged += (_, _) => _okButton!.Enabled = _list.SelectedItem is not null;
        _list.DoubleClick += (_, _) => Confirm();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(_list, 0, 1);

        _okButton = new Button { Text = localization.Get("Common.Ok"), Width = 100, Height = 32, Enabled = false };
        _okButton.Click += (_, _) => Confirm();
        var cancelButton = new Button { Text = localization.Get("Common.Cancel"), DialogResult = DialogResult.Cancel, Width = 100, Height = 32 };
        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(_okButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = _okButton;
        CancelButton = cancelButton;

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private void Confirm()
    {
        if (_list.SelectedItem is not Item item) return;
        Result = item.Candidate;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record Item(RestockCandidate Candidate, string Text)
    {
        public override string ToString() => Text;
    }
}
