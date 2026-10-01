using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.UI.UiExtensions;

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

        // Docked layout: header, list and a right-aligned button row
        // that grows with its captions, so no button is cut at any text
        // size or display scaling (the fixed 80 x 23 buttons were).
        var header = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = _loc.Get("Ui.ProfilePickerForm.Header"),
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.Name"), 220);
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.Role"), 100);
        _list.Columns.Add(_loc.Get("Ui.ProfilePickerForm.Column.LastUsed"), 180);
        _list.DoubleClick += (_, _) => TryOpenSelection();

        _openButton = DialogButton(_loc.Get("Ui.ProfilePickerForm.Open"));
        _openButton.Enabled = false;
        _cancelButton = DialogButton(_loc.Get("Common.Cancel"));
        _cancelButton.DialogResult = DialogResult.Cancel;
        _openButton.Click += (_, _) => TryOpenSelection();

        AcceptButton = _openButton;
        CancelButton = _cancelButton;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, UiTheme.Space.M, 0, 0),
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_openButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(UiTheme.Space.L),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);

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

    private static Button DialogButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new System.Drawing.Size(88, 32),
        Padding = new Padding(UiTheme.Space.M, 0, UiTheme.Space.M, 0),
        Margin = new Padding(UiTheme.Space.S, 0, 0, 0),
    };

    private void TryOpenSelection()
    {
        if (_list.SelectedItems.Count != 1) return;
        var profile = (Profile)_list.SelectedItems[0].Tag!;
        SelectedProfile = profile;
        DialogResult = DialogResult.OK;
        Close();
    }
}
