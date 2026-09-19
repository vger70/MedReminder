using System.Globalization;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Boot-time profile picker (docs/ANALYSIS-MULTI-USER.md §12.1).
// Shown when the registry has more than one profile OR when the
// user explicitly triggers "File → Change profile…" (15d).
// Sorted by LastUsedAt descending so the "most recently used"
// profile is the default.
//
// A single "Open" button — profile management (create / rename /
// delete / PIN) lives inside the app (§12.1) and is added by 15d as
// ProfilesManagerForm.
internal sealed class ProfilePickerForm : MedReminderFormBase
{
    private readonly IProfileRegistry _registry;
    private readonly ILocalizationService _loc;
    private readonly ListView _list;
    private readonly Button _openButton;
    private readonly Button _cancelButton;

    public ProfilePickerForm(IProfileRegistry registry, ILocalizationService loc)
    {
        _registry = registry;
        _loc = loc;

        Text = _loc.Get("Ui.ProfilePickerForm.Title");
        Width = 560;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var header = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ProfilePickerForm.Header"),
            Location = new System.Drawing.Point(16, 12),
            MaximumSize = new System.Drawing.Size(520, 0),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Location = new System.Drawing.Point(16, 44),
            Size = new System.Drawing.Size(520, 260),
        };
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.Name"), 220);
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.Role"), 100);
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.LastUsed"), 180);
        _list.DoubleClick += (_, _) => TryOpenSelection();

        _openButton = new Button
        {
            Text = _loc.Get("Ui.ProfilePickerForm.Open"),
            Location = new System.Drawing.Point(360, 320),
            Width = 80,
            Enabled = false,
        };
        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(456, 320),
            Width = 80,
        };
        _openButton.Click += (_, _) => TryOpenSelection();

        AcceptButton = _openButton;
        CancelButton = _cancelButton;

        Controls.Add(header);
        Controls.Add(_list);
        Controls.Add(_openButton);
        Controls.Add(_cancelButton);

        _list.SelectedIndexChanged += (_, _) =>
            _openButton.Enabled = _list.SelectedItems.Count == 1;

        LoadProfiles();
    }

    // The profile chosen by the user. null when the dialog was
    // cancelled or dismissed without picking anything.
    public Profile? SelectedProfile { get; private set; }

    private void LoadProfiles()
    {
        _list.Items.Clear();
        var profiles = _registry.ListProfiles()
            .OrderByDescending(p => p.LastUsedAt)
            .ToList();

        var hint = _registry.ActiveProfileIdHint;
        ListViewItem? hinted = null;
        foreach (var p in profiles)
        {
            var item = new ListViewItem(p.DisplayName)
            {
                Tag = p,
            };
            item.SubItems.Add(_loc.Get(p.Role == ProfileRole.Admin
                ? "Ui.ProfilePickerForm.Role.Admin"
                : "Ui.ProfilePickerForm.Role.User"));
            // "Last used" format: dd/MM HH:mm — decision D
            // (docs/ANALYSIS-MULTI-USER.md §14 D).
            item.SubItems.Add(p.LastUsedAt.LocalDateTime.ToString(
                "dd/MM HH:mm", CultureInfo.InvariantCulture));
            _list.Items.Add(item);
            if (string.Equals(p.Id, hint, StringComparison.Ordinal))
            {
                hinted = item;
            }
        }

        if (hinted is not null)
        {
            hinted.Selected = true;
            hinted.EnsureVisible();
        }
        else if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }
    }

    private void TryOpenSelection()
    {
        if (_list.SelectedItems.Count != 1) return;
        var profile = (Profile)_list.SelectedItems[0].Tag!;
        SelectedProfile = profile;
        DialogResult = DialogResult.OK;
        Close();
    }
}
