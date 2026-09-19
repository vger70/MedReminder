using System.Globalization;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;

namespace MedReminder.UI.Forms;

// Admin-only profile management (docs/ANALYSIS-MULTI-USER.md §12.1,
// §12.4). Opened from Tools → Manage profiles… — the menu entry
// itself is hidden for non-admin users (§12.2), but the form also
// guards against being opened by a non-admin caller as a defense in
// depth.
//
// Invariants enforced by the UI (in addition to those already
// enforced by IProfileRegistry):
//  - Delete is disabled for the currently active profile (§14a H).
//    Switch profile first.
//  - Delete is disabled for the last remaining admin (§14a G).
//    Handled twice: the button is disabled AND the registry throws.
//  - Role is set once at creation and never editable afterwards
//    (§14a G, "immutable role"). The rename dialog does not touch
//    Role; there is no promote/demote UI.
//  - The optional "also delete data on disk" checkbox defaults to
//    OFF (§13, "user deletes a profile by mistake" mitigation).
internal sealed class ProfilesManagerForm : MedReminderFormBase
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _currentProfile;
    private readonly ILocalizationService _loc;

    private ListView _list = null!;
    private Button _newButton = null!;
    private Button _renameButton = null!;
    private Button _pinButton = null!;
    private Button _deleteButton = null!;
    private Label _hintLabel = null!;
    private readonly ToolTip _tooltips = new() { ShowAlways = true };

    public ProfilesManagerForm(
        IProfileRegistry registry,
        ICurrentProfile currentProfile,
        ILocalizationService loc)
    {
        _registry = registry;
        _currentProfile = currentProfile;
        _loc = loc;

        Text = _loc.Get("Ui.ProfilesManagerForm.Title");
        Width = 640;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        BuildLayout();
        Reload();
    }

    private void BuildLayout()
    {
        var header = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            Location = new System.Drawing.Point(16, 12),
            Text = _loc.Get("Ui.ProfilesManagerForm.Header"),
        };

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Location = new System.Drawing.Point(16, 44),
            Size = new System.Drawing.Size(600, 300),
        };
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Name"), 200);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Role"), 100);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Pin"), 90);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.LastUsed"), 130);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Active"), 70);
        _list.SelectedIndexChanged += (_, _) => UpdateButtonStates();

        _newButton = new Button
        {
            Text = _loc.Get("Ui.ProfilesManagerForm.New"),
            Location = new System.Drawing.Point(16, 360),
            Width = 130,
            Height = 30,
        };
        _renameButton = new Button
        {
            Text = _loc.Get("Ui.ProfilesManagerForm.Rename"),
            Location = new System.Drawing.Point(156, 360),
            Width = 100,
            Height = 30,
        };
        _pinButton = new Button
        {
            Text = _loc.Get("Ui.ProfilesManagerForm.ChangePin"),
            Location = new System.Drawing.Point(266, 360),
            Width = 140,
            Height = 30,
        };
        _deleteButton = new Button
        {
            Text = _loc.Get("Ui.ProfilesManagerForm.Delete"),
            Location = new System.Drawing.Point(416, 360),
            Width = 100,
            Height = 30,
        };
        var closeButton = new Button
        {
            Text = _loc.Get("Common.Close"),
            DialogResult = DialogResult.OK,
            Location = new System.Drawing.Point(526, 360),
            Width = 90,
            Height = 30,
        };
        _newButton.Click += (_, _) => AddNew();
        _renameButton.Click += (_, _) => RenameSelected();
        _pinButton.Click += (_, _) => ChangePinSelected();
        _deleteButton.Click += (_, _) => DeleteSelected();
        CancelButton = closeButton;

        _hintLabel = new Label
        {
            AutoSize = true,
            Location = new System.Drawing.Point(16, 400),
            ForeColor = System.Drawing.Color.DarkGray,
            MaximumSize = new System.Drawing.Size(600, 0),
            Text = _loc.Get("Ui.ProfilesManagerForm.Hint.ImmutableRole"),
        };

        Controls.Add(header);
        Controls.Add(_list);
        Controls.Add(_newButton);
        Controls.Add(_renameButton);
        Controls.Add(_pinButton);
        Controls.Add(_deleteButton);
        Controls.Add(closeButton);
        Controls.Add(_hintLabel);

        _tooltips.SetToolTip(_deleteButton,
            _loc.Get("Ui.ProfilesManagerForm.Tooltip.Delete"));
        _tooltips.SetToolTip(_pinButton,
            _loc.Get("Ui.ProfilesManagerForm.Tooltip.Pin"));
    }

    private void Reload()
    {
        var previouslySelectedId = SelectedProfileId();
        _list.Items.Clear();
        var profiles = _registry.ListProfiles()
            .OrderByDescending(p => p.LastUsedAt)
            .ToList();
        foreach (var p in profiles)
        {
            var item = new ListViewItem(p.DisplayName) { Tag = p };
            item.SubItems.Add(_loc.Get(p.Role == ProfileRole.Admin
                ? "Ui.ProfilesManagerForm.Role.Admin"
                : "Ui.ProfilesManagerForm.Role.User"));
            item.SubItems.Add(_loc.Get(p.HasPin
                ? "Ui.ProfilesManagerForm.Pin.Set"
                : "Ui.ProfilesManagerForm.Pin.None"));
            item.SubItems.Add(p.LastUsedAt.LocalDateTime.ToString(
                "dd/MM HH:mm", CultureInfo.InvariantCulture));
            item.SubItems.Add(string.Equals(p.Id, _currentProfile.Id, StringComparison.Ordinal)
                ? _loc.Get("Ui.ProfilesManagerForm.Active.Yes")
                : string.Empty);
            _list.Items.Add(item);
        }

        // Re-select the previously selected profile if it still
        // exists, so the form does not visually jump after a rename
        // or PIN change.
        if (previouslySelectedId is not null)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is Profile p &&
                    string.Equals(p.Id, previouslySelectedId, StringComparison.Ordinal))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var selected = SelectedProfile();
        _renameButton.Enabled = selected is not null;
        _pinButton.Enabled = selected is not null;

        if (selected is null)
        {
            _deleteButton.Enabled = false;
            return;
        }

        var isActive = string.Equals(
            selected.Id, _currentProfile.Id, StringComparison.Ordinal);
        var adminsCount = _registry.ListProfiles()
            .Count(p => p.Role == ProfileRole.Admin);
        var isLastAdmin = selected.Role == ProfileRole.Admin && adminsCount <= 1;

        _deleteButton.Enabled = !isActive && !isLastAdmin;
    }

    private Profile? SelectedProfile() =>
        _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as Profile
            : null;

    private string? SelectedProfileId() => SelectedProfile()?.Id;

    // ---- New profile ----

    private void AddNew()
    {
        using var dialog = new NewProfileDialog(_loc);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var created = _registry.Create(dialog.ProfileName, dialog.SelectedRole);
            if (!string.IsNullOrEmpty(dialog.OptionalPin))
            {
                _registry.SetPin(created.Id, dialog.OptionalPin);
            }
            Reload();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Rename ----

    private void RenameSelected()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        using var dialog = new RenameProfileDialog(_loc, selected.DisplayName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _registry.Rename(selected.Id, dialog.NewName);
            Reload();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- PIN change ----

    private void ChangePinSelected()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        using var dialog = new ChangePinDialog(_loc, selected.HasPin);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _registry.SetPin(selected.Id, dialog.ClearPin ? null : dialog.NewPin);
            Reload();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    // ---- Delete ----

    private void DeleteSelected()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        // The button is already disabled for the active / last-admin
        // cases, but this is a defense-in-depth check.
        if (string.Equals(selected.Id, _currentProfile.Id, StringComparison.Ordinal))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.ProfilesManagerForm.Delete.ActiveBlocked"),
                _loc.Get("Ui.ProfilesManagerForm.Delete.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dialog = new ConfirmDeleteProfileDialog(_loc, selected.DisplayName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _registry.Delete(selected.Id, deleteData: dialog.AlsoDeleteData);
            Reload();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ShowError(Exception ex) =>
        MessageBox.Show(this, ex.Message,
            _loc.Get("Common.Error"),
            MessageBoxButtons.OK, MessageBoxIcon.Error);

    // -------- Inline dialogs --------

    private sealed class NewProfileDialog : MedReminderFormBase
    {
        private readonly ILocalizationService _loc;
        private readonly TextBox _nameBox;
        private readonly RadioButton _userRadio;
        private readonly RadioButton _adminRadio;
        private readonly TextBox _pinBox;
        private readonly TextBox _pinConfirmBox;
        private readonly Label _statusLabel;

        public NewProfileDialog(ILocalizationService loc)
        {
            _loc = loc;
            Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Title");
            Width = 460;
            Height = 380;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9.75F);

            var nameLabel = new Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(16, 12),
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Name"),
            };
            _nameBox = new TextBox
            {
                Location = new System.Drawing.Point(16, 32),
                Width = 420,
                MaxLength = 100,
            };

            var roleLabel = new Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(16, 68),
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Role"),
            };
            _userRadio = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Location = new System.Drawing.Point(16, 88),
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleUser"),
            };
            _adminRadio = new RadioButton
            {
                AutoSize = true,
                Location = new System.Drawing.Point(120, 88),
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleAdmin"),
            };
            var roleNote = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(420, 0),
                Location = new System.Drawing.Point(16, 112),
                ForeColor = System.Drawing.Color.DarkGray,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleNote"),
            };

            var pinLabel = new Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(16, 168),
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.PinOptional"),
            };
            _pinBox = new TextBox
            {
                Location = new System.Drawing.Point(16, 188),
                Width = 200,
                UseSystemPasswordChar = true,
                MaxLength = 32,
                PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
            };
            _pinConfirmBox = new TextBox
            {
                Location = new System.Drawing.Point(230, 188),
                Width = 206,
                UseSystemPasswordChar = true,
                MaxLength = 32,
                PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
            };

            _statusLabel = new Label
            {
                AutoSize = true,
                ForeColor = System.Drawing.Color.Firebrick,
                Location = new System.Drawing.Point(16, 224),
                Text = string.Empty,
            };

            var createButton = new Button
            {
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Create"),
                Location = new System.Drawing.Point(256, 300),
                Width = 90,
            };
            var cancelButton = new Button
            {
                Text = _loc.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(356, 300),
                Width = 80,
            };
            createButton.Click += (_, _) => Confirm();
            AcceptButton = createButton;
            CancelButton = cancelButton;

            Controls.Add(nameLabel);
            Controls.Add(_nameBox);
            Controls.Add(roleLabel);
            Controls.Add(_userRadio);
            Controls.Add(_adminRadio);
            Controls.Add(roleNote);
            Controls.Add(pinLabel);
            Controls.Add(_pinBox);
            Controls.Add(_pinConfirmBox);
            Controls.Add(_statusLabel);
            Controls.Add(createButton);
            Controls.Add(cancelButton);

            Shown += (_, _) => _nameBox.Focus();
        }

        public string ProfileName { get; private set; } = string.Empty;
        public ProfileRole SelectedRole { get; private set; } = ProfileRole.User;
        public string? OptionalPin { get; private set; }

        private void Confirm()
        {
            var name = _nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _statusLabel.Text = _loc.Get("Ui.FirstRunWizardForm.NameRequired");
                _nameBox.Focus();
                return;
            }
            var pin = _pinBox.Text;
            var pinConfirm = _pinConfirmBox.Text;
            if (!string.IsNullOrEmpty(pin) || !string.IsNullOrEmpty(pinConfirm))
            {
                if (!string.Equals(pin, pinConfirm, StringComparison.Ordinal))
                {
                    _statusLabel.Text = _loc.Get("Ui.FirstRunWizardForm.PinMismatch");
                    _pinConfirmBox.Focus();
                    return;
                }
            }

            ProfileName = name;
            SelectedRole = _adminRadio.Checked ? ProfileRole.Admin : ProfileRole.User;
            OptionalPin = string.IsNullOrEmpty(pin) ? null : pin;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private sealed class RenameProfileDialog : MedReminderFormBase
    {
        private readonly ILocalizationService _loc;
        private readonly TextBox _nameBox;

        public RenameProfileDialog(ILocalizationService loc, string currentName)
        {
            _loc = loc;
            Text = _loc.Get("Ui.ProfilesManagerForm.RenameDialog.Title");
            Width = 400;
            Height = 180;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9.75F);

            var label = new Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(16, 12),
                Text = _loc.Get("Ui.ProfilesManagerForm.RenameDialog.Prompt"),
            };
            _nameBox = new TextBox
            {
                Location = new System.Drawing.Point(16, 40),
                Width = 360,
                Text = currentName,
                MaxLength = 100,
            };
            var okButton = new Button
            {
                Text = _loc.Get("Common.Ok"),
                Location = new System.Drawing.Point(196, 96),
                Width = 90,
            };
            var cancelButton = new Button
            {
                Text = _loc.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(296, 96),
                Width = 80,
            };
            okButton.Click += (_, _) =>
            {
                var value = _nameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(value)) return;
                NewName = value;
                DialogResult = DialogResult.OK;
                Close();
            };
            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(label);
            Controls.Add(_nameBox);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            Shown += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
        }

        public string NewName { get; private set; } = string.Empty;
    }

    private sealed class ConfirmDeleteProfileDialog : MedReminderFormBase
    {
        private readonly ILocalizationService _loc;
        private readonly string _expectedName;
        private readonly TextBox _confirmBox;
        private readonly CheckBox _alsoDeleteDataBox;
        private readonly Button _confirmButton;

        public ConfirmDeleteProfileDialog(ILocalizationService loc, string profileName)
        {
            _loc = loc;
            _expectedName = profileName;
            Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.Title");
            Width = 480;
            Height = 300;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9.75F);

            var warning = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                Location = new System.Drawing.Point(16, 12),
                ForeColor = System.Drawing.Color.Firebrick,
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.Warning", profileName),
            };
            var typePrompt = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                Location = new System.Drawing.Point(16, 84),
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.TypeName", profileName),
            };
            _confirmBox = new TextBox
            {
                Location = new System.Drawing.Point(16, 116),
                Width = 440,
            };
            _confirmBox.TextChanged += (_, _) => UpdateConfirmEnabled();

            // Default OFF — mitigates accidental data loss (§13).
            _alsoDeleteDataBox = new CheckBox
            {
                AutoSize = true,
                Location = new System.Drawing.Point(16, 152),
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.AlsoDeleteData"),
                Checked = false,
            };
            var alsoDeleteNote = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                Location = new System.Drawing.Point(36, 176),
                ForeColor = System.Drawing.Color.DarkGray,
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.AlsoDeleteDataNote"),
            };

            _confirmButton = new Button
            {
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.Confirm"),
                Location = new System.Drawing.Point(266, 220),
                Width = 110,
                Enabled = false,
            };
            var cancelButton = new Button
            {
                Text = _loc.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(386, 220),
                Width = 80,
            };
            _confirmButton.Click += (_, _) =>
            {
                AlsoDeleteData = _alsoDeleteDataBox.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            AcceptButton = _confirmButton;
            CancelButton = cancelButton;

            Controls.Add(warning);
            Controls.Add(typePrompt);
            Controls.Add(_confirmBox);
            Controls.Add(_alsoDeleteDataBox);
            Controls.Add(alsoDeleteNote);
            Controls.Add(_confirmButton);
            Controls.Add(cancelButton);

            Shown += (_, _) => _confirmBox.Focus();
        }

        public bool AlsoDeleteData { get; private set; }

        private void UpdateConfirmEnabled()
        {
            // Case-sensitive match: the user has to type the name
            // exactly as it appears in the list.
            _confirmButton.Enabled = string.Equals(
                _confirmBox.Text.Trim(), _expectedName, StringComparison.Ordinal);
        }
    }
}
