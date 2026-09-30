using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;

namespace MedReminder.UI.Forms;

// Shown when the join passphrase opens more than one sync group of the
// storage (one group per profile). The groups are told apart by their
// devices: the profile name is encrypted in the operations and is only
// known after the join.
internal sealed class SyncGroupChoiceDialog : MedReminderFormBase
{
    private readonly ListBox _groups;
    private readonly IReadOnlyList<SyncGroupCandidate> _candidates;

    public SyncGroupChoiceDialog(ILocalizationService localization, IReadOnlyList<SyncGroupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));

        Text = localization.Get("Ui.SyncDialog.Join.ChooseTitle");
        Width = 560;
        Height = 340;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var hint = new Label
        {
            Text = localization.Get("Ui.SyncDialog.Join.ChooseHint"),
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Dock = DockStyle.Top,
            Padding = new Padding(12, 12, 12, 8),
        };

        _groups = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var candidate in candidates)
        {
            _groups.Items.Add(Describe(localization, candidate));
        }
        if (_groups.Items.Count > 0) _groups.SelectedIndex = 0;
        _groups.DoubleClick += (_, _) => Accept();
        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        list.Controls.Add(_groups);

        var ok = DialogLayout.Button(localization.Get("Common.Ok"));
        ok.Click += (_, _) => Accept();
        var cancel = DialogLayout.Button(localization.Get("Common.Cancel"), DialogResult.Cancel);
        var buttons = DialogLayout.ButtonBar(this, ok, cancel);

        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(buttons);
    }

    public Guid? SelectedGroupId
        => _groups.SelectedIndex >= 0 ? _candidates[_groups.SelectedIndex].GroupId : null;

    private void Accept()
    {
        if (SelectedGroupId is null) return;
        DialogResult = DialogResult.OK;
    }

    // "PC A, PC B — last seen 28/09/2026 10:15"; newest device first.
    private static string Describe(ILocalizationService localization, SyncGroupCandidate candidate)
    {
        if (candidate.Devices.Count == 0) return localization.Get("Ui.SyncDialog.Join.ChooseNoDevices");
        var names = string.Join(", ", candidate.Devices.Select(d => d.Name));
        var lastSeen = candidate.Devices.Max(d => d.LastSeen).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return localization.Get("Ui.SyncDialog.Join.ChooseItem", names, lastSeen);
    }
}
