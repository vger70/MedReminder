using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Export;
using MedReminder.Application.UseCases;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Settings;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Controls;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Settings → Notifications (the section list and the dialog frame are in
// SettingsDialog.cs).
internal sealed partial class SettingsDialog
{
    // Notifications section (Increment 15d).
    // Per-profile "where do the emails go" tab (§7.4). Visible to
    // every profile: an admin sees it in addition to the Email tab
    // (SMTP transport); a non-admin user sees only this tab and
    // relies on the admin for the SMTP configuration itself.
    private Panel BuildNotificationsTab()
    {
        var page = new Panel();
        var current = _notificationMonitor.CurrentValue;

        _toBox = new TextBox { Dock = DockStyle.Fill, Text = current.ToAddress };
        _tooltips.SetToolTip(_toBox, _loc.Get("Ui.SettingsDialog.Tooltip.To"));

        // A3: optional secondary recipient. Empty = no caregiver (§3.1).
        _caregiverBox = new TextBox { Dock = DockStyle.Fill, Text = current.CaregiverAddress };

        var caregiverHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.Notifications.CaregiverAddress.Help"),
        };

        _doctorBox = new TextBox { Dock = DockStyle.Fill, Text = current.DoctorAddress };

        var doctorHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get("Ui.SettingsDialog.Notifications.DoctorAddress.Help"),
        };

        var saveButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.Notifications.Save"),
            AutoSize = true,
            Height = 28,
        };
        saveButton.Click += async (_, _) => await SaveNotificationSettingsAsync(saveButton);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = UiColors.Hint,
            Text = _loc.Get(_currentProfile.IsAdmin
                ? "Ui.SettingsDialog.Notifications.NoteAdmin"
                : "Ui.SettingsDialog.Notifications.NoteUser"),
        };

        var table = BuildFormTable();
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.To"), _toBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Notifications.CaregiverAddress.Label"), _caregiverBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Notifications.DoctorAddress.Label"), _doctorBox);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12),
        };
        buttons.Controls.Add(saveButton);

        var container = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
            AutoScroll = true,
        };
        container.Controls.Add(table);
        container.Controls.Add(caregiverHelp);
        container.Controls.Add(doctorHelp);
        container.Controls.Add(buttons);
        container.Controls.Add(explanation);
        container.Controls.Add(BuildMyPinSection());
        // The flow's scroll range stops at the last control and ignores
        // its bottom padding; a spacer keeps the group off the edge.
        container.Controls.Add(new Panel { Height = UiTheme.Space.L, Width = 1, Margin = Padding.Empty });

        page.Controls.Add(container);
        return page;
    }

    // Self-service PIN management for the current profile. Renders
    // on the Notifications tab because that is the only settings
    // page visible to non-admin profiles — the admin-only
    // ProfilesManagerForm can still change PINs for any profile
    // (§8).
    private GroupBox BuildMyPinSection()
    {
        var group = new GroupBox
        {
            Text = _loc.Get("Ui.SettingsDialog.Notifications.MyPin"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            Margin = new Padding(0, 12, 0, 0),
        };

        _pinStateLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
            Text = FormatPinStateText(),
        };

        var changeButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.Notifications.SetPin"),
            AutoSize = true,
            Margin = Padding.Empty,
        };
        changeButton.Click += async (_, _) => await ChangeMyPinAsync();

        // A flow inside the group instead of fixed positions: the group
        // sizes to what the label and button need after scaling, so its
        // bottom edge is no longer cut with Large text.
        var content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        content.Controls.Add(_pinStateLabel);
        content.Controls.Add(changeButton);
        group.Controls.Add(content);
        return group;
    }

    private string FormatPinStateText()
    {
        var stateKey = _profileRegistry.HasPin(_currentProfile.Id)
            ? "Ui.SettingsDialog.Notifications.PinStateSet"
            : "Ui.SettingsDialog.Notifications.PinStateNone";
        return _loc.Get(stateKey);
    }

    // Household step H2: through SetProfilePin, which records the PIN
    // hash in the household.
    private async Task ChangeMyPinAsync()
    {
        try
        {
            var hasPin = _profileRegistry.HasPin(_currentProfile.Id);
            using var dialog = new ChangePinDialog(_loc, hasPin);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            if (_scopes is null) throw new InvalidOperationException("The settings dialog has no service scope.");
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<SetProfilePin>().ExecuteAsync(
                    _currentProfile.Id, dialog.ClearPin ? null : dialog.NewPin, CancellationToken.None);
            }
            if (IsDisposed) return;

            _pinStateLabel.Text = FormatPinStateText();
            UiMessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Notifications.PinChanged"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UiMessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // B.1, P8: through UpdateNotificationSettings, which records the
    // change for the other devices of a synced profile and writes
    // notifications.settings.json.
    private async Task SaveNotificationSettingsAsync(Button saveButton)
    {
        saveButton.Enabled = false;
        try
        {
            var toAddress = _toBox.Text.Trim();
            var caregiverAddress = _caregiverBox.Text.Trim();
            var doctorAddress = _doctorBox.Text.Trim();

            // A3 (§5.2): a non-empty caregiver address must parse as a
            // well-formed mailbox and must not equal the primary
            // (case-insensitive). An empty value is allowed
            // (unconfigured). On failure, surface an inline error and
            // abort the save.
            if (caregiverAddress.Length > 0)
            {
                if (!MimeKit.MailboxAddress.TryParse(
                        _addressParserOptions, caregiverAddress, out _))
                {
                    UiMessageBox.Show(this,
                        _loc.Get("Ui.SettingsDialog.Notifications.CaregiverAddress.Invalid"),
                        _loc.Get("Ui.SettingsDialog.Notifications.SaveError"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.Equals(caregiverAddress, toAddress, StringComparison.OrdinalIgnoreCase))
                {
                    UiMessageBox.Show(this,
                        _loc.Get("Ui.SettingsDialog.Notifications.CaregiverAddress.SameAsPrimary"),
                        _loc.Get("Ui.SettingsDialog.Notifications.SaveError"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // Doctor address: same well-formedness rule as the
            // caregiver (a full mailbox with a domain). Empty allowed.
            if (doctorAddress.Length > 0
                && !MimeKit.MailboxAddress.TryParse(_addressParserOptions, doctorAddress, out _))
            {
                UiMessageBox.Show(this,
                    _loc.Get("Ui.SettingsDialog.Notifications.DoctorAddress.Invalid"),
                    _loc.Get("Ui.SettingsDialog.Notifications.SaveError"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_scopes is null) throw new InvalidOperationException("The settings dialog has no service scope.");
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<UpdateNotificationSettings>()
                    .ExecuteAsync(toAddress, caregiverAddress, doctorAddress, CancellationToken.None);
            }
            if (IsDisposed) return;
            UiMessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Notifications.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                UiMessageBox.Show(this, ex.Message,
                    _loc.Get("Ui.SettingsDialog.Notifications.SaveError"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (!IsDisposed) saveButton.Enabled = true;
        }
    }
}
