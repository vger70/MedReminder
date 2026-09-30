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

// Settings → Email (the section list and the dialog frame are in
// SettingsDialog.cs).
internal sealed partial class SettingsDialog
{
    // Email section.
    private Panel BuildEmailTab()
    {
        // Admin-only tab (§7.4). The per-profile ToAddress moved to
        // the Notifications tab in Increment 15d; the Email tab now
        // only holds the global SMTP transport configuration.
        var page = new Panel();
        var current = _smtpMonitor.CurrentValue;

        _hostBox = new TextBox { Dock = DockStyle.Fill, Text = current.Host };
        _portBox = new NumericUpDown { Dock = DockStyle.Left, Width = 100, Minimum = 1, Maximum = 65535, Value = current.Port > 0 ? current.Port : 587 };
        _useTlsBox = new CheckBox { Text = _loc.Get("Ui.SettingsDialog.Email.UseTls"), AutoSize = true, Checked = current.UseStartTls };
        _usernameBox = new TextBox { Dock = DockStyle.Fill, Text = current.Username };
        _passwordBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = _loc.Get("Ui.SettingsDialog.Email.PasswordPlaceholder") };
        _clearPasswordBox = new CheckBox { Text = _loc.Get("Ui.SettingsDialog.Email.ClearPassword"), AutoSize = true };
        _fromBox = new TextBox { Dock = DockStyle.Fill, Text = current.FromAddress };
        _fromNameBox = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrEmpty(current.FromDisplayName) ? "MedReminder" : current.FromDisplayName };
        _timeoutBox = new NumericUpDown { Dock = DockStyle.Left, Width = 100, Minimum = 5, Maximum = 300, Value = current.TimeoutSeconds > 0 ? current.TimeoutSeconds : 30 };

        _tooltips.SetToolTip(_hostBox, _loc.Get("Ui.SettingsDialog.Tooltip.Host"));
        _tooltips.SetToolTip(_portBox, _loc.Get("Ui.SettingsDialog.Tooltip.Port"));
        _tooltips.SetToolTip(_useTlsBox, _loc.Get("Ui.SettingsDialog.Tooltip.UseTls"));
        _tooltips.SetToolTip(_usernameBox, _loc.Get("Ui.SettingsDialog.Tooltip.Username"));
        _tooltips.SetToolTip(_passwordBox, _loc.Get("Ui.SettingsDialog.Tooltip.Password"));
        _tooltips.SetToolTip(_clearPasswordBox, _loc.Get("Ui.SettingsDialog.Tooltip.ClearPassword"));
        _tooltips.SetToolTip(_fromBox, _loc.Get("Ui.SettingsDialog.Tooltip.From"));
        _tooltips.SetToolTip(_fromNameBox, _loc.Get("Ui.SettingsDialog.Tooltip.FromName"));
        _tooltips.SetToolTip(_timeoutBox, _loc.Get("Ui.SettingsDialog.Tooltip.Timeout"));

        _passwordStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = _credentialStore.HasPassword ? UiColors.Success : UiColors.Hint,
            Text = _loc.Get(_credentialStore.HasPassword
                ? "Ui.SettingsDialog.Email.PasswordStored"
                : "Ui.SettingsDialog.Email.PasswordEmpty"),
        };

        var testButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Email.Test"), AutoSize = true, Height = 28 };
        var saveButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Email.Save"), AutoSize = true, Height = 28 };
        testButton.Click += async (_, _) => await TestSmtpAsync(testButton);
        saveButton.Click += async (_, _) => await SaveSmtpSettingsAsync();

        var table = BuildFormTable();
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.Host"), _hostBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.Port"), _portBox);
        AddRow(table, string.Empty, _useTlsBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.Username"), _usernameBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.NewPassword"), _passwordBox);
        AddRow(table, string.Empty, _passwordStatusLabel);
        AddRow(table, string.Empty, _clearPasswordBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.From"), _fromBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.FromName"), _fromNameBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.Timeout"), _timeoutBox);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12) };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(testButton);

        var container = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        container.Controls.Add(table);

        page.Controls.Add(container);
        page.Controls.Add(buttons);
        return page;
    }

    // Household step H2b: through UpdateSmtpSettings, which writes the
    // transport and the password and records them in the household.
    // Returns false when the save failed (the error was shown).
    private async Task<bool> SaveSmtpSettingsAsync()
    {
        try
        {
            var settings = new SmtpTransport(
                Host: _hostBox.Text.Trim(),
                Port: (int)_portBox.Value,
                UseStartTls: _useTlsBox.Checked,
                Username: _usernameBox.Text.Trim(),
                FromAddress: _fromBox.Text.Trim(),
                FromDisplayName: _fromNameBox.Text.Trim(),
                TimeoutSeconds: (int)_timeoutBox.Value);

            // Password: what the user typed replaces the stored one; empty
            // keeps it. The "clear" checkbox wins and removes it.
            var clear = _clearPasswordBox.Checked;
            var typed = _passwordBox.Text;
            await RunUseCaseAsync<UpdateSmtpSettings>(u => u.ExecuteAsync(
                settings, string.IsNullOrEmpty(typed) ? null : typed, clear, CancellationToken.None));
            if (IsDisposed) return true;
            _passwordStatusLabel.Text = _loc.Get(_credentialStore.HasPassword
                ? "Ui.SettingsDialog.Email.PasswordStored"
                : "Ui.SettingsDialog.Email.PasswordEmpty");
            _passwordBox.Text = string.Empty;
            _clearPasswordBox.Checked = false;

            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Email.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(this, ex.Message,
                    _loc.Get("Ui.SettingsDialog.Email.SaveError"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }
    }

    private async Task TestSmtpAsync(Button button)
    {
        // Save first because TestConnectionAsync operates on the
        // current settings (IOptionsMonitor refreshes after the file
        // is written).
        if (!await SaveSmtpSettingsAsync()) return;
        // Household step H4c (§7.6): the settings are the installation's
        // and stay editable here; only the master sends, so only the
        // master tests the connection.
        if (_scopes is not null)
        {
            await using var scope = _scopes.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IMasterRole>() is { } master
                && !await master.SendsEmailAsync(CancellationToken.None))
            {
                MessageBox.Show(this, _loc.Get("Ui.SettingsDialog.Email.NotMaster"),
                    _loc.Get("Ui.SettingsDialog.Email.TestTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        button.Enabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ok = await _emailService.TestConnectionAsync(cts.Token);
            var msg = _loc.Get(ok
                ? "Ui.SettingsDialog.Email.TestOk"
                : "Ui.SettingsDialog.Email.TestFailed");
            var icon = ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(this, msg,
                _loc.Get("Ui.SettingsDialog.Email.TestTitle"),
                MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Email.TestTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }
}
