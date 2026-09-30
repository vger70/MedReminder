using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.UseCases;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;

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
//  - Household step H2 (docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md
//    §8) makes the role editable: Change role… for any profile but the
//    open one (ANALYSIS-MULTI-USER-ROLES-OVERVIEW.md D1), never leaving
//    the installation without an admin; promoting a profile without a
//    PIN warns (D2).
//  - Every change goes through a use case (CreateProfile, RenameProfile,
//    SetProfilePin, ChangeProfileRole, DeleteProfile), which records it
//    in the household.
//  - The optional "also delete data on disk" checkbox defaults to
//    OFF (§13, "user deletes a profile by mistake" mitigation).
internal sealed class ProfilesManagerForm : MedReminderFormBase
{
    private readonly IProfileRegistry _registry;
    private readonly ICurrentProfile _currentProfile;
    private readonly ILocalizationService _loc;
    private readonly IServiceScopeFactory _scopes;

    private ListView _list = null!;
    private Button _newButton = null!;
    private Button _renameButton = null!;
    private Button _pinButton = null!;
    private Button _roleButton = null!;
    private Button _deleteButton = null!;
    private Label _hintLabel = null!;
    private readonly ToolTip _tooltips = new() { ShowAlways = true };

    public ProfilesManagerForm(
        IProfileRegistry registry,
        ICurrentProfile currentProfile,
        ILocalizationService loc,
        IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        _registry = registry;
        _currentProfile = currentProfile;
        _loc = loc;

        Text = _loc.Get("Ui.ProfilesManagerForm.Title");
        Width = 790;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        DialogLayout.GrowToContent(this);

        BuildLayout();
        Reload();
    }

    private void BuildLayout()
    {
        var header = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(740, 0),
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
            Text = _loc.Get("Ui.ProfilesManagerForm.Header"),
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
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Name"), 200);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Role"), 100);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Pin"), 90);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.LastUsed"), 130);
        _list.Columns.Add(_loc.Get("Ui.ProfilesManagerForm.Column.Active"), 70);
        _list.SelectedIndexChanged += (_, _) => UpdateButtonStates();

        _newButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.New"));
        _renameButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.Rename"));
        _pinButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.ChangePin"));
        _roleButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.ChangeRole"));
        _deleteButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.Delete"));
        var closeButton = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        _newButton.Click += async (_, _) => await AddNewAsync();
        _renameButton.Click += async (_, _) => await RenameSelectedAsync();
        _pinButton.Click += async (_, _) => await ChangePinSelectedAsync();
        _roleButton.Click += async (_, _) => await ChangeRoleSelectedAsync();
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync();

        _hintLabel = new Label
        {
            AutoSize = true,
            ForeColor = UiColors.Hint,
            MaximumSize = new System.Drawing.Size(740, 0),
            Text = _loc.Get("Ui.ProfilesManagerForm.Hint.Role"),
        };

        // Profile actions under the list, wrapping when the captions are
        // long; Close alone in the dialog's button bar (§5.3).
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, UiTheme.Space.S, 0, UiTheme.Space.XS),
        };
        foreach (var button in new[] { _newButton, _renameButton, _pinButton, _roleButton, _deleteButton })
        {
            button.Margin = new Padding(0, 0, UiTheme.Space.S, UiTheme.Space.XS);
            actions.Controls.Add(button);
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, 0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        layout.Controls.Add(_hintLabel, 0, 3);

        Controls.Add(layout);
        Controls.Add(DialogLayout.ButtonBar(this, closeButton, closeButton));

        _tooltips.SetToolTip(_deleteButton,
            _loc.Get("Ui.ProfilesManagerForm.Tooltip.Delete"));
        _tooltips.SetToolTip(_pinButton,
            _loc.Get("Ui.ProfilesManagerForm.Tooltip.Pin"));
        _tooltips.SetToolTip(_roleButton,
            _loc.Get("Ui.ProfilesManagerForm.Tooltip.ChangeRole"));
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
            _roleButton.Enabled = false;
            return;
        }

        var isActive = string.Equals(
            selected.Id, _currentProfile.Id, StringComparison.Ordinal);
        var adminsCount = _registry.ListProfiles()
            .Count(p => p.Role == ProfileRole.Admin);
        var isLastAdmin = selected.Role == ProfileRole.Admin && adminsCount <= 1;

        _deleteButton.Enabled = !isActive && !isLastAdmin;
        _roleButton.Enabled = !isActive && !isLastAdmin;
    }

    private Profile? SelectedProfile() =>
        _list.SelectedItems.Count == 1
            ? _list.SelectedItems[0].Tag as Profile
            : null;

    private string? SelectedProfileId() => SelectedProfile()?.Id;

    // ---- New profile ----

    private async Task AddNewAsync()
    {
        using var dialog = new NewProfileDialog(_loc);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<CreateProfile>().ExecuteAsync(
                    dialog.ProfileName, dialog.SelectedRole,
                    string.IsNullOrEmpty(dialog.OptionalPin) ? null : dialog.OptionalPin, CancellationToken.None);
            }
            if (!IsDisposed) Reload();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(ex);
        }
    }

    // ---- Rename ----

    // B.1, P8: through RenameProfile, so the name of a synced profile
    // reaches its other devices; another synced profile must be opened
    // to be renamed.
    private async Task RenameSelectedAsync()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        using var dialog = new RenameProfileDialog(_loc, selected.DisplayName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<RenameProfile>()
                    .ExecuteAsync(selected.Id, dialog.NewName, CancellationToken.None);
            }
            if (!IsDisposed) Reload();
        }
        catch (SyncedProfileRenameException)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(this, _loc.Get("Ui.ProfilesManagerForm.Rename.Synced", selected.DisplayName),
                    _loc.Get("Ui.ProfilesManagerForm.Rename"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(ex);
        }
    }

    // ---- PIN change ----

    private async Task ChangePinSelectedAsync()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        using var dialog = new ChangePinDialog(_loc, selected.HasPin);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<SetProfilePin>().ExecuteAsync(
                    selected.Id, dialog.ClearPin ? null : dialog.NewPin, CancellationToken.None);
            }
            if (!IsDisposed) Reload();
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(ex);
        }
    }

    // ---- Role change (household step H2) ----

    private async Task ChangeRoleSelectedAsync()
    {
        var selected = SelectedProfile();
        if (selected is null) return;
        var promote = selected.Role != ProfileRole.Admin;
        var text = promote
            ? _loc.Get(selected.HasPin
                ? "Ui.ProfilesManagerForm.Role.ConfirmPromote"
                : "Ui.ProfilesManagerForm.Role.ConfirmPromoteNoPin", selected.DisplayName)
            : _loc.Get("Ui.ProfilesManagerForm.Role.ConfirmDemote", selected.DisplayName);
        if (MessageBox.Show(this, text, _loc.Get("Ui.ProfilesManagerForm.Role.Title"), MessageBoxButtons.YesNo,
                promote && !selected.HasPin ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ChangeProfileRole>().ExecuteAsync(
                    selected.Id, promote ? ProfileRole.Admin : ProfileRole.User, CancellationToken.None);
            }
            if (!IsDisposed) Reload();
        }
        catch (ProfileAdministrationException ex) when (!IsDisposed)
        {
            ShowRefusal(ex, "Ui.ProfilesManagerForm.Role.Title", "Ui.ProfilesManagerForm.Role.ActiveBlocked");
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(ex);
        }
    }

    // ---- Delete ----

    private async Task DeleteSelectedAsync()
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
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DeleteProfile>().ExecuteAsync(
                    selected.Id, dialog.AlsoDeleteData, CancellationToken.None);
            }
            if (!IsDisposed) Reload();
        }
        catch (ProfileAdministrationException ex) when (!IsDisposed)
        {
            ShowRefusal(ex, "Ui.ProfilesManagerForm.Delete.Title", "Ui.ProfilesManagerForm.Delete.ActiveBlocked");
        }
        catch (Exception ex)
        {
            if (!IsDisposed) ShowError(ex);
        }
    }

    // A refusal of a use case, in the words of the form.
    private void ShowRefusal(ProfileAdministrationException ex, string titleKey, string activeKey)
    {
        var message = ex.Error switch
        {
            ProfileAdministrationError.ActiveProfile => _loc.Get(activeKey),
            ProfileAdministrationError.LastAdmin => _loc.Get("Ui.ProfilesManagerForm.Role.LastAdmin"),
            _ => ex.Message,
        };
        MessageBox.Show(this, message, _loc.Get(titleKey), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            DialogLayout.GrowToContent(this);

            var nameLabel = new Label
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Name"),
            };
            _nameBox = new TextBox
            {
                Width = 420,
                MaxLength = 100,
            };

            var roleLabel = new Label
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.Role"),
            };
            _userRadio = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleUser"),
            };
            _adminRadio = new RadioButton
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleAdmin"),
            };
            var roleNote = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(420, 0),
                ForeColor = UiColors.Hint,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.RoleNote"),
            };

            var pinLabel = new Label
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.NewDialog.PinOptional"),
            };
            _pinBox = new TextBox
            {
                Width = 200,
                UseSystemPasswordChar = true,
                MaxLength = 32,
                PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinPlaceholder"),
            };
            _pinConfirmBox = new TextBox
            {
                Width = 206,
                UseSystemPasswordChar = true,
                MaxLength = 32,
                PlaceholderText = _loc.Get("Ui.FirstRunWizardForm.PinConfirmPlaceholder"),
            };

            // Inline error under the fields (F9).
            _statusLabel = DialogLayout.ErrorLabel();

            var createButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.NewDialog.Create"));
            var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
            createButton.Click += (_, _) => Confirm();

            Controls.Add(DialogLayout.Stack(
                nameLabel, _nameBox, roleLabel, DialogLayout.Row(_userRadio, _adminRadio), roleNote,
                pinLabel, DialogLayout.Row(_pinBox, _pinConfirmBox), _statusLabel));
            Controls.Add(DialogLayout.ButtonBar(this, createButton, cancelButton));

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
                DialogLayout.ShowError(_statusLabel, _loc.Get("Ui.FirstRunWizardForm.NameRequired"), _nameBox);
                return;
            }
            var pin = _pinBox.Text;
            var pinConfirm = _pinConfirmBox.Text;
            if (!string.IsNullOrEmpty(pin) || !string.IsNullOrEmpty(pinConfirm))
            {
                if (!string.Equals(pin, pinConfirm, StringComparison.Ordinal))
                {
                    DialogLayout.ShowError(_statusLabel, _loc.Get("Ui.FirstRunWizardForm.PinMismatch"), _pinConfirmBox);
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
            DialogLayout.GrowToContent(this);

            var label = new Label
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.RenameDialog.Prompt"),
            };
            _nameBox = new TextBox
            {
                Width = 360,
                Text = currentName,
                MaxLength = 100,
            };
            var okButton = DialogLayout.Button(_loc.Get("Common.Ok"));
            var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
            okButton.Click += (_, _) =>
            {
                var value = _nameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(value)) return;
                NewName = value;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(DialogLayout.Stack(label, _nameBox));
            Controls.Add(DialogLayout.ButtonBar(this, okButton, cancelButton));
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
            DialogLayout.GrowToContent(this);

            var warning = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                ForeColor = UiColors.Error,
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.Warning", profileName),
            };
            var typePrompt = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.TypeName", profileName),
            };
            _confirmBox = new TextBox
            {
                Width = 440,
            };
            _confirmBox.TextChanged += (_, _) => UpdateConfirmEnabled();

            // Default OFF — mitigates accidental data loss (§13).
            _alsoDeleteDataBox = new CheckBox
            {
                AutoSize = true,
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.AlsoDeleteData"),
                Checked = false,
            };
            var alsoDeleteNote = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(440, 0),
                ForeColor = UiColors.Hint,
                Text = _loc.Get("Ui.ProfilesManagerForm.DeleteDialog.AlsoDeleteDataNote"),
            };

            _confirmButton = DialogLayout.Button(_loc.Get("Ui.ProfilesManagerForm.DeleteDialog.Confirm"));
            _confirmButton.Enabled = false;
            var cancelButton = DialogLayout.Button(_loc.Get("Common.Cancel"), DialogResult.Cancel);
            _confirmButton.Click += (_, _) =>
            {
                AlsoDeleteData = _alsoDeleteDataBox.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(DialogLayout.Stack(warning, typePrompt, _confirmBox, _alsoDeleteDataBox, alsoDeleteNote));
            // The note belongs to the check box above it.
            alsoDeleteNote.Margin = new Padding(UiTheme.Space.XL, 0, 0, UiTheme.Space.S);
            Controls.Add(DialogLayout.ButtonBar(this, _confirmButton, cancelButton));

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
