using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Settings split into tabs:
//   Email  — SMTP host / port / TLS / user, DPAPI-protected
//            password, send test.
//   Startup — auto-start with Windows.
//   Backup — DB export / import.
//
// The editable SMTP fields are serialized to
// %LOCALAPPDATA%\MedReminder\smtp.settings.json (added to the
// IConfiguration chain in Program.cs with reloadOnChange=true, so
// IOptionsMonitor<SmtpSettings> refreshes without a restart).
internal sealed class SettingsDialog : MedReminderFormBase
{
    private readonly IOptionsMonitor<SmtpSettings> _smtpMonitor;
    private readonly IOptionsMonitor<NotificationSettings> _notificationMonitor;
    private readonly IOptionsMonitor<BackupSettings> _backupMonitor;
    private readonly IOptionsMonitor<UserSettings> _userMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly IEmailNotificationService _emailService;
    private readonly IAutoStartService _autoStart;
    private readonly IBackupService _backup;
    private readonly IBackupStateStore _backupState;
    private readonly IApplicationRestarter _restarter;
    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry _profileRegistry;
    private readonly ILocalizationService _loc;
    private readonly IReferenceCatalogueQueryService _catalogueQuery;

    // Email tab controls
    private TextBox _hostBox = null!;
    private NumericUpDown _portBox = null!;
    private CheckBox _useTlsBox = null!;
    private TextBox _usernameBox = null!;
    private TextBox _passwordBox = null!;
    private CheckBox _clearPasswordBox = null!;
    private TextBox _fromBox = null!;
    private TextBox _fromNameBox = null!;
    private TextBox _toBox = null!;
    private NumericUpDown _timeoutBox = null!;
    private Label _passwordStatusLabel = null!;

    // Auto-start
    private CheckBox _autoStartCheck = null!;

    // Backup
    private Label _dbPathLabel = null!;
    private CheckBox _backupEnabledBox = null!;
    private TextBox _backupDirectoryBox = null!;
    private DateTimePicker _backupTimePicker = null!;
    private NumericUpDown _backupRetentionBox = null!;
    private Label _backupStatusLabel = null!;
    private Label _backupCloudWarningLabel = null!;

    // Generale (Incremento 16b) — selezione lingua UI
    private ComboBox _languageCombo = null!;
    // Reference-catalogue country (M2). Dropdown populated with
    // countries actually present in the local catalogue plus a
    // synthetic "EU" entry for supranational authorisations.
    private ComboBox _referenceCountryCombo = null!;

    // Shared component for the explanatory tooltips on the technical fields.
    // (spec Incremento 14: help in linea, tooltip diffusi). Un solo
    // ToolTip per dialog è la best practice WinForms.
    private readonly ToolTip _tooltips = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 200,
        ShowAlways = true,
    };

    public SettingsDialog(
        IOptionsMonitor<SmtpSettings> smtpMonitor,
        IOptionsMonitor<NotificationSettings> notificationMonitor,
        IOptionsMonitor<BackupSettings> backupMonitor,
        IOptionsMonitor<UserSettings> userMonitor,
        ISmtpCredentialStore credentialStore,
        IEmailNotificationService emailService,
        IAutoStartService autoStart,
        IBackupService backup,
        IBackupStateStore backupState,
        IApplicationRestarter restarter,
        ICurrentProfile currentProfile,
        IProfileRegistry profileRegistry,
        ILocalizationService localization,
        IReferenceCatalogueQueryService catalogueQuery)
    {
        _smtpMonitor = smtpMonitor;
        _notificationMonitor = notificationMonitor;
        _backupMonitor = backupMonitor;
        _userMonitor = userMonitor;
        _credentialStore = credentialStore;
        _emailService = emailService;
        _autoStart = autoStart;
        _backup = backup;
        _backupState = backupState;
        _restarter = restarter;
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;
        _loc = localization;
        _catalogueQuery = catalogueQuery;

        Text = _loc.Get("Ui.SettingsDialog.Title");
        Width = 620;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        // Increment 15d (docs/ANALYSIS-MULTI-USER.md §7.4): SMTP and
        // Backup tabs are admin-only. Every profile still needs to
        // choose its own recipient — that lives in the new
        // Notifications tab, visible to admins and users alike.
        if (_currentProfile.IsAdmin)
        {
            tabs.TabPages.Add(BuildEmailTab());
        }
        tabs.TabPages.Add(BuildNotificationsTab());
        tabs.TabPages.Add(BuildStartupTab());
        if (_currentProfile.IsAdmin)
        {
            tabs.TabPages.Add(BuildBackupTab());
        }

        var closeButton = new Button { Text = _loc.Get("Common.Close"), DialogResult = DialogResult.OK, Width = 100, Height = 32 };
        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.Add(closeButton);

        Controls.Add(tabs);
        Controls.Add(buttonPanel);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    // ------------------ General tab (Incremento 16b) ------------------
    private TabPage BuildGeneralTab()
    {
        var page = new TabPage(_loc.Get("Ui.SettingsDialog.Tab.General"));

        var languageLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.General.Language"),
        };

        // ComboBox con Items = SupportedLanguage records. DisplayMember
        // = DisplayName localized for the current language.
        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        foreach (var lang in SupportedLanguages.All)
        {
            var localizedName = _loc.Get(LanguageDisplayKey(lang.Code));
            _languageCombo.Items.Add(new LanguageChoice(lang.Code, localizedName));
            if (string.Equals(lang.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _languageCombo.SelectedIndex = _languageCombo.Items.Count - 1;
            }
        }
        _languageCombo.DisplayMember = nameof(LanguageChoice.DisplayName);

        _tooltips.SetToolTip(_languageCombo, _loc.Get("Ui.SettingsDialog.Tooltip.Language"));

        // Reference country (M2). Sits under the language row.
        var referenceCountryLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("settings.referenceCountry.label"),
        };
        _referenceCountryCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        PopulateReferenceCountryCombo();
        _tooltips.SetToolTip(_referenceCountryCombo, _loc.Get("settings.referenceCountry.help"));

        var referenceCountryHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("settings.referenceCountry.help"),
        };

        var saveButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.General.Save"),
            AutoSize = true,
            Height = 30,
        };
        saveButton.Click += (_, _) => SaveGeneral();

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("Ui.SettingsDialog.General.Note"),
        };

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
        };
        panel.Controls.Add(languageLabel);
        panel.Controls.Add(_languageCombo);
        panel.Controls.Add(referenceCountryLabel);
        panel.Controls.Add(_referenceCountryCombo);
        panel.Controls.Add(referenceCountryHelp);
        panel.Controls.Add(saveButton);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }

    // Fill the reference-country dropdown with the distinct countries
    // present in the local catalogue plus the synthetic "EU" entry
    // (supranational). "IT" is always offered even on an empty DB so
    // the user has something meaningful to pick before the first
    // snapshot import completes.
    private void PopulateReferenceCountryCombo()
    {
        var options = new SortedSet<string>(StringComparer.Ordinal) { "IT", "EU" };
        try
        {
            var present = _catalogueQuery
                .ListAvailableCountriesAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            foreach (var code in present)
            {
                options.Add(code.Value);
            }
        }
        catch
        {
            // Best-effort: an empty or unavailable catalogue must not
            // stop the Settings dialog from opening.
        }

        var selected = _userMonitor.CurrentValue.ReferenceCountry ?? "IT";
        foreach (var option in options)
        {
            _referenceCountryCombo.Items.Add(option);
            if (string.Equals(option, selected, StringComparison.OrdinalIgnoreCase))
            {
                _referenceCountryCombo.SelectedIndex = _referenceCountryCombo.Items.Count - 1;
            }
        }
        if (_referenceCountryCombo.SelectedIndex < 0 && _referenceCountryCombo.Items.Count > 0)
        {
            _referenceCountryCombo.SelectedIndex = 0;
        }
    }

    private void SaveGeneral()
    {
        if (_languageCombo.SelectedItem is not LanguageChoice choice) return;

        var referenceCountry = _referenceCountryCombo.SelectedItem as string ?? "IT";
        var settings = new UserSettings
        {
            Language = choice.Code,
            ReferenceCountry = referenceCountry,
        };

        try
        {
            WriteUserSettingsToDisk(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // If the language did not change, no restart needed. A
        // ReferenceCountry change alone is picked up at the next
        // opening of the medicine form via IOptionsMonitor
        // (user.settings.json is watched with reloadOnChange=true).
        if (string.Equals(choice.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.General.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.SettingsDialog.General.RestartPrompt"),
            _loc.Get("Ui.SettingsDialog.General.RestartPrompt.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            _restarter.RestartAndExit();
        }
    }

    private static void WriteUserSettingsToDisk(UserSettings settings)
    {
        var payload = new { UI = settings };
        var path = Path.Combine(AppDataPaths.GetAppDataDirectory(), "user.settings.json");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }

    private sealed record LanguageChoice(string Code, string DisplayName);

    // Mappa un codice ISO 639-1 sulla chiave JSON che restituisce il
    // the language name in the current UI language. Unknown codes
    // ricadono su Language.English (fail-safe).
    private static string LanguageDisplayKey(string code) => code switch
    {
        "en" => "Language.English",
        "it" => "Language.Italian",
        "fr" => "Language.French",
        "es" => "Language.Spanish",
        "de" => "Language.German",
        _ => "Language.English",
    };

    // ------------------ Email tab ------------------
    private TabPage BuildEmailTab()
    {
        // Admin-only tab (§7.4). The per-profile ToAddress moved to
        // the Notifications tab in Increment 15d; the Email tab now
        // only holds the global SMTP transport configuration.
        var page = new TabPage(_loc.Get("Ui.SettingsDialog.Tab.Email"));
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
            ForeColor = _credentialStore.HasPassword ? System.Drawing.Color.DarkGreen : System.Drawing.Color.DarkGray,
            Text = _loc.Get(_credentialStore.HasPassword
                ? "Ui.SettingsDialog.Email.PasswordStored"
                : "Ui.SettingsDialog.Email.PasswordEmpty"),
        };

        var testButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Email.Test"), AutoSize = true, Height = 28 };
        var saveButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Email.Save"), AutoSize = true, Height = 28 };
        testButton.Click += async (_, _) => await TestSmtpAsync(testButton);
        saveButton.Click += (_, _) => SaveSmtpSettings();

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

    private void SaveSmtpSettings()
    {
        try
        {
            var settings = new SmtpSettings
            {
                Host = _hostBox.Text.Trim(),
                Port = (int)_portBox.Value,
                UseStartTls = _useTlsBox.Checked,
                Username = _usernameBox.Text.Trim(),
                FromAddress = _fromBox.Text.Trim(),
                FromDisplayName = _fromNameBox.Text.Trim(),
                TimeoutSeconds = (int)_timeoutBox.Value,
            };

            // Password: if the user has typed something, encrypt it;
            // otherwise keep the current one. The "clear" checkbox
            // ha precedenza e rimuove la password.
            if (_clearPasswordBox.Checked)
            {
                _credentialStore.Clear();
            }
            else if (!string.IsNullOrEmpty(_passwordBox.Text))
            {
                _credentialStore.SetPassword(_passwordBox.Text);
            }

            WriteSmtpSettingsToDisk(settings);
            _passwordStatusLabel.Text = _loc.Get(_credentialStore.HasPassword
                ? "Ui.SettingsDialog.Email.PasswordStored"
                : "Ui.SettingsDialog.Email.PasswordEmpty");
            _passwordBox.Text = string.Empty;
            _clearPasswordBox.Checked = false;

            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Email.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Email.SaveError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task TestSmtpAsync(Button button)
    {
        // Save first because TestConnectionAsync operates on the
        // current settings (IOptionsMonitor refreshes after the file
        // is written).
        SaveSmtpSettings();
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

    private static void WriteSmtpSettingsToDisk(SmtpSettings settings)
    {
        var payload = new { Smtp = settings };
        var path = Path.Combine(AppDataPaths.GetAppDataDirectory(), "smtp.settings.json");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }

    // Writes the per-profile notifications.settings.json. Introduced
    // in Increment 15c (docs/ANALYSIS-MULTI-USER.md §7.1) alongside
    // the SmtpSettings.ToAddress removal.
    private static void WriteNotificationSettingsToDisk(
        NotificationSettings settings, string path)
    {
        var payload = new { Notifications = settings };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }

    // ------------------ Notifications tab (Increment 15d) ------------------
    // Per-profile "where do the emails go" tab (§7.4). Visible to
    // every profile: an admin sees it in addition to the Email tab
    // (SMTP transport); a non-admin user sees only this tab and
    // relies on the admin for the SMTP configuration itself.
    private TabPage BuildNotificationsTab()
    {
        var page = new TabPage(_loc.Get("Ui.SettingsDialog.Tab.Notifications"));
        var current = _notificationMonitor.CurrentValue;

        _toBox = new TextBox { Dock = DockStyle.Fill, Text = current.ToAddress };
        _tooltips.SetToolTip(_toBox, _loc.Get("Ui.SettingsDialog.Tooltip.To"));

        var saveButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.Notifications.Save"),
            AutoSize = true,
            Height = 28,
        };
        saveButton.Click += (_, _) => SaveNotificationSettings();

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get(_currentProfile.IsAdmin
                ? "Ui.SettingsDialog.Notifications.NoteAdmin"
                : "Ui.SettingsDialog.Notifications.NoteUser"),
        };

        var table = BuildFormTable();
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.To"), _toBox);

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
            AutoScroll = true,
        };
        container.Controls.Add(table);
        container.Controls.Add(buttons);
        container.Controls.Add(explanation);

        page.Controls.Add(container);
        return page;
    }

    private void SaveNotificationSettings()
    {
        try
        {
            var settings = new NotificationSettings
            {
                ToAddress = _toBox.Text.Trim(),
            };
            WriteNotificationSettingsToDisk(settings, _currentProfile.NotificationSettingsPath);
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Notifications.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Notifications.SaveError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------ Startup tab ------------------
    private TabPage BuildStartupTab()
    {
        var page = new TabPage(_loc.Get("Ui.SettingsDialog.Tab.Startup"));

        _autoStartCheck = new CheckBox
        {
            Text = _loc.Get("Ui.SettingsDialog.Startup.AutoStart"),
            AutoSize = true,
            Checked = _autoStart.IsEnabled,
        };
        _autoStartCheck.CheckedChanged += (_, _) =>
        {
            try
            {
                if (_autoStartCheck.Checked) _autoStart.Enable();
                else _autoStart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message,
                    _loc.Get("Ui.SettingsDialog.Startup.Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _autoStartCheck.Checked = _autoStart.IsEnabled;
            }
        };

        var note = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.Startup.Note"),
            ForeColor = System.Drawing.Color.DarkGray,
        };

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
        };
        panel.Controls.Add(_autoStartCheck);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }

    // ------------------ Backup tab ------------------
    private TabPage BuildBackupTab()
    {
        var page = new TabPage(_loc.Get("Ui.SettingsDialog.Tab.Backup"));
        var settings = _backupMonitor.CurrentValue;

        _dbPathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            Text = _loc.Get("Ui.SettingsDialog.Backup.DbPath", _backup.DatabasePath),
            ForeColor = System.Drawing.Color.DarkGray,
        };

        _backupEnabledBox = new CheckBox
        {
            Text = _loc.Get("Ui.SettingsDialog.Backup.Enable"),
            AutoSize = true,
            Checked = settings.Enabled,
        };

        _backupDirectoryBox = new TextBox
        {
            Width = 400,
            Text = settings.Directory,
            ReadOnly = false,
        };
        var browseButton = new Button { Text = _loc.Get("Common.Browse"), AutoSize = true };
        browseButton.Click += (_, _) => BrowseBackupDirectory();

        // DateTimePicker in modalità "Time": mostra solo HH:mm (custom
        // format), prevent the user from changing the date.
        _backupTimePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "HH:mm",
            ShowUpDown = true,
            Width = 100,
        };
        _backupTimePicker.Value = ParsePreferredTimeAsDateTime(settings.PreferredTime);

        _backupRetentionBox = new NumericUpDown
        {
            Width = 80,
            Minimum = 0,
            Maximum = 3650,
            Value = settings.RetentionDays > 0 ? settings.RetentionDays : 30,
        };

        _tooltips.SetToolTip(_backupEnabledBox, _loc.Get("Ui.SettingsDialog.Tooltip.BackupEnabled"));
        _tooltips.SetToolTip(_backupDirectoryBox, _loc.Get("Ui.SettingsDialog.Tooltip.BackupDirectory"));
        _tooltips.SetToolTip(_backupTimePicker, _loc.Get("Ui.SettingsDialog.Tooltip.BackupTime"));
        _tooltips.SetToolTip(_backupRetentionBox, _loc.Get("Ui.SettingsDialog.Tooltip.BackupRetention"));
        _tooltips.SetToolTip(browseButton, _loc.Get("Ui.SettingsDialog.Tooltip.BackupBrowse"));

        var saveButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.SaveSettings"), AutoSize = true, Height = 30 };
        saveButton.Click += (_, _) => SaveBackupSettings();

        var runNowButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.RunNow"), AutoSize = true, Height = 30 };
        runNowButton.Click += async (_, _) => await RunBackupNowAsync(runNowButton);

        var exportButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.ExportCustom"), AutoSize = true, Height = 30 };
        exportButton.Click += async (_, _) => await ExportBackupAsync(exportButton);

        var importButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.Restore"), AutoSize = true, Height = 30 };
        importButton.Click += async (_, _) => await ImportBackupAsync(importButton);

        _backupStatusLabel = new Label { AutoSize = true };
        UpdateBackupStatusLabel();

        _backupCloudWarningLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            ForeColor = System.Drawing.Color.DarkOrange,
            Text = string.Empty,
            Visible = false,
        };
        UpdateCloudWarning();
        _backupDirectoryBox.TextChanged += (_, _) => UpdateCloudWarning();

        var directoryRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
        };
        directoryRow.Controls.Add(_backupDirectoryBox);
        directoryRow.Controls.Add(browseButton);

        var table = BuildFormTable();
        AddRow(table, string.Empty, _backupEnabledBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.Directory"), directoryRow);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.PreferredTime"), _backupTimePicker);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.RetentionDays"), _backupRetentionBox);
        AddRow(table, string.Empty, _backupStatusLabel);
        AddRow(table, string.Empty, _backupCloudWarningLabel);

        var actionButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
        };
        actionButtons.Controls.Add(saveButton);
        actionButtons.Controls.Add(runNowButton);
        actionButtons.Controls.Add(exportButton);
        actionButtons.Controls.Add(importButton);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            AutoEllipsis = false,
            Text = _loc.Get("Ui.SettingsDialog.Backup.Note"),
            ForeColor = System.Drawing.Color.DarkGray,
        };

        var container = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            AutoScroll = true,
        };
        container.Controls.Add(_dbPathLabel);
        container.Controls.Add(table);
        container.Controls.Add(actionButtons);
        container.Controls.Add(note);
        page.Controls.Add(container);
        return page;
    }

    private void BrowseBackupDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.SettingsDialog.Backup.BrowseDialog.Title"),
            InitialDirectory = string.IsNullOrWhiteSpace(_backupDirectoryBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _backupDirectoryBox.Text,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _backupDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void SaveBackupSettings()
    {
        try
        {
            var directory = _backupDirectoryBox.Text.Trim();
            var enabled = _backupEnabledBox.Checked;

            if (enabled && string.IsNullOrWhiteSpace(directory))
            {
                MessageBox.Show(this,
                    _loc.Get("Ui.SettingsDialog.Backup.NoDirectorySelected"),
                    _loc.Get("Ui.SettingsDialog.Backup.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(directory))
            {
                // Create the folder if missing — immediate feedback to the user.
                try { Directory.CreateDirectory(directory); }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        _loc.Get("Ui.SettingsDialog.Backup.DirectoryCreateError", ex.Message),
                        _loc.Get("Ui.SettingsDialog.Backup.Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            var settings = new BackupSettings
            {
                Enabled = enabled,
                Directory = directory,
                PreferredTime = _backupTimePicker.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                RetentionDays = (int)_backupRetentionBox.Value,
            };

            WriteBackupSettingsToDisk(settings);
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Backup.Saved"),
                _loc.Get("Common.Ok"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Backup.SaveError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunBackupNowAsync(Button button)
    {
        var directory = _backupDirectoryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Backup.RunNoDir"),
                _loc.Get("Ui.SettingsDialog.Backup.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        button.Enabled = false;
        try
        {
            var file = await _backup.ExportProfileAsync(
                _currentProfile.Id, directory, CancellationToken.None);
            var retention = (int)_backupRetentionBox.Value;
            var pruned = retention > 0
                ? await _backup.PruneOldBackupsAsync(directory, retention, CancellationToken.None)
                : 0;

            _backupState.Save(new BackupState(
                LastSuccessfulBackupAt: DateTimeOffset.UtcNow,
                LastAttemptAt: DateTimeOffset.UtcNow,
                LastError: null,
                LastBackupFile: file));
            UpdateBackupStatusLabel();

            var suffix = pruned > 0 ? _loc.Get("Ui.SettingsDialog.Backup.RunPruned", pruned) : string.Empty;
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Backup.RunSuccess", file) + suffix,
                _loc.Get("Ui.SettingsDialog.Backup.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _backupState.Save(new BackupState(
                LastSuccessfulBackupAt: _backupState.Load().LastSuccessfulBackupAt,
                LastAttemptAt: DateTimeOffset.UtcNow,
                LastError: ex.Message,
                LastBackupFile: null));
            UpdateBackupStatusLabel();
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Backup.RunError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task ExportBackupAsync(Button button)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.SettingsDialog.Backup.ExportDialog.Title"),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        button.Enabled = false;
        try
        {
            var file = await _backup.ExportProfileAsync(
                _currentProfile.Id, dialog.SelectedPath, CancellationToken.None);
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Backup.RunSuccess", file),
                _loc.Get("Ui.SettingsDialog.Backup.ExportTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Backup.ExportError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task ImportBackupAsync(Button button)
    {
        using var fileDialog = new OpenFileDialog
        {
            Title = _loc.Get("Ui.SettingsDialog.Backup.FileDialog.Title"),
            Filter = _loc.Get("Ui.SettingsDialog.Backup.FileDialog.Filter"),
            CheckFileExists = true,
        };
        if (fileDialog.ShowDialog(this) != DialogResult.OK) return;

        // Increment 15d (docs/ANALYSIS-MULTI-USER.md §11.3): after
        // picking the .db, show a chooser dialog with a dropdown of
        // known profiles. Default to the profileId extracted from the
        // filename (medreminder-<profileId>-YYYYMMDD-HHmmss.db) so
        // the common case is a one-click restore into the profile
        // the backup originally came from.
        var profiles = _profileRegistry.ListProfiles();
        var fileName = Path.GetFileName(fileDialog.FileName);
        var extractedId = TryExtractProfileIdFromBackupName(fileName);
        using var chooser = new RestoreIntoProfileDialog(
            _loc, profiles, extractedId, _currentProfile.Id);
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        var targetProfileId = chooser.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(targetProfileId)) return;

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.SettingsDialog.Backup.RestoreConfirm"),
            _loc.Get("Ui.SettingsDialog.Backup.RestoreConfirmTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        button.Enabled = false;
        try
        {
            await _backup.ImportProfileAsync(
                targetProfileId, fileDialog.FileName, CancellationToken.None);

            var isActive = string.Equals(
                targetProfileId, _currentProfile.Id, StringComparison.Ordinal);
            MessageBox.Show(this,
                _loc.Get(isActive
                    ? "Ui.SettingsDialog.Backup.RestoreDone"
                    : "Ui.SettingsDialog.Backup.RestoreDoneInactive"),
                _loc.Get("Ui.SettingsDialog.Backup.ImportTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // §11.2: only restart when the imported profile is the
            // active one — the process is still holding the old DB
            // open through EF Core. Restoring an inactive profile
            // does not touch the live connection.
            if (isActive)
            {
                _restarter.RestartAndExit();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Ui.SettingsDialog.Backup.ImportError"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    // Parses "medreminder-<profileId>-YYYYMMDD-HHmmss.db" and returns
    // the profileId, or null when the filename does not follow the
    // convention (user-renamed backup, pre-15c filename, etc.).
    private static string? TryExtractProfileIdFromBackupName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"^medreminder-(?<profileId>[0-9a-fA-F]{32}|default)-\d{8}-\d{6}\.db$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["profileId"].Value : null;
    }

    // Nested chooser dialog for the restore-into-profile UX (§11.3).
    // Sits close to the caller to keep the wiring one-file; the
    // logic is trivial enough that a separate top-level class would
    // be overkill.
    private sealed class RestoreIntoProfileDialog : MedReminderFormBase
    {
        private readonly ComboBox _combo;

        public RestoreIntoProfileDialog(
            ILocalizationService loc,
            IReadOnlyList<Profile> profiles,
            string? filenameProfileId,
            string activeProfileId)
        {
            Text = loc.Get("Ui.SettingsDialog.Backup.RestoreInto.Title");
            Width = 460;
            Height = 260;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new System.Drawing.Font("Segoe UI", 9.75F);

            var prompt = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(420, 0),
                Location = new System.Drawing.Point(16, 12),
                Text = loc.Get("Ui.SettingsDialog.Backup.RestoreInto.Prompt"),
            };

            _combo = new ComboBox
            {
                Location = new System.Drawing.Point(16, 56),
                Width = 420,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            foreach (var p in profiles)
            {
                var label = p.DisplayName;
                if (string.Equals(p.Id, activeProfileId, StringComparison.Ordinal))
                {
                    label = loc.Get("Ui.SettingsDialog.Backup.RestoreInto.ActiveSuffix", label);
                }
                _combo.Items.Add(new ProfileItem(p.Id, label));
            }
            // Default selection: filename profileId first, then the
            // active profile as a safe fallback.
            int defaultIndex = -1;
            if (!string.IsNullOrWhiteSpace(filenameProfileId))
            {
                for (int i = 0; i < _combo.Items.Count; i++)
                {
                    if (((ProfileItem)_combo.Items[i]!).Id
                            .Equals(filenameProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        defaultIndex = i;
                        break;
                    }
                }
            }
            if (defaultIndex < 0)
            {
                for (int i = 0; i < _combo.Items.Count; i++)
                {
                    if (((ProfileItem)_combo.Items[i]!).Id
                            .Equals(activeProfileId, StringComparison.Ordinal))
                    {
                        defaultIndex = i;
                        break;
                    }
                }
            }
            if (defaultIndex >= 0) _combo.SelectedIndex = defaultIndex;

            var extractedNote = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(420, 0),
                Location = new System.Drawing.Point(16, 96),
                ForeColor = System.Drawing.Color.DarkGray,
                Text = string.IsNullOrWhiteSpace(filenameProfileId)
                    ? loc.Get("Ui.SettingsDialog.Backup.RestoreInto.NoFilenameHint")
                    : loc.Get("Ui.SettingsDialog.Backup.RestoreInto.FilenameHint", filenameProfileId),
            };

            var okButton = new Button
            {
                Text = loc.Get("Common.Ok"),
                Location = new System.Drawing.Point(256, 172),
                Width = 90,
            };
            var cancelButton = new Button
            {
                Text = loc.Get("Common.Cancel"),
                DialogResult = DialogResult.Cancel,
                Location = new System.Drawing.Point(356, 172),
                Width = 80,
            };
            okButton.Click += (_, _) =>
            {
                if (_combo.SelectedItem is ProfileItem picked)
                {
                    SelectedProfileId = picked.Id;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            AcceptButton = okButton;
            CancelButton = cancelButton;

            Controls.Add(prompt);
            Controls.Add(_combo);
            Controls.Add(extractedNote);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
        }

        public string? SelectedProfileId { get; private set; }

        private sealed record ProfileItem(string Id, string Label)
        {
            public override string ToString() => Label;
        }
    }

    private void UpdateBackupStatusLabel()
    {
        var state = _backupState.Load();
        if (state.LastSuccessfulBackupAt is null && state.LastAttemptAt is null)
        {
            _backupStatusLabel.ForeColor = System.Drawing.Color.DarkGray;
            _backupStatusLabel.Text = _loc.Get("Ui.SettingsDialog.Backup.NoBackupsYet");
            return;
        }

        if (state.LastSuccessfulBackupAt is { } ok)
        {
            var okLocal = ok.ToLocalTime();
            var timestamp = okLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            var errorSuffix = state.LastError is not null
                ? _loc.Get("Ui.SettingsDialog.Backup.LastError", state.LastError)
                : string.Empty;
            _backupStatusLabel.ForeColor = state.LastError is null
                ? System.Drawing.Color.DarkGreen
                : System.Drawing.Color.DarkOrange;
            _backupStatusLabel.Text =
                _loc.Get("Ui.SettingsDialog.Backup.LastOk", timestamp) + errorSuffix;
            return;
        }

        _backupStatusLabel.ForeColor = System.Drawing.Color.Firebrick;
        _backupStatusLabel.Text = state.LastError is not null
            ? _loc.Get("Ui.SettingsDialog.Backup.LastFailedWithMessage", state.LastError)
            : _loc.Get("Ui.SettingsDialog.Backup.LastFailedGeneric");
    }

    private void UpdateCloudWarning()
    {
        var path = _backupDirectoryBox.Text ?? string.Empty;
        var isCloud =
            path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Dropbox", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Google Drive", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase);
        if (isCloud)
        {
            _backupCloudWarningLabel.Text = _loc.Get("Ui.SettingsDialog.Backup.CloudWarning");
            _backupCloudWarningLabel.Visible = true;
        }
        else
        {
            _backupCloudWarningLabel.Text = string.Empty;
            _backupCloudWarningLabel.Visible = false;
        }
    }

    private static void WriteBackupSettingsToDisk(BackupSettings settings)
    {
        var payload = new { Backup = settings };
        var path = Path.Combine(AppDataPaths.GetAppDataDirectory(), "backup.settings.json");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(path, json);
    }

    private static DateTime ParsePreferredTimeAsDateTime(string raw)
    {
        // DateTimePicker needs a full DateTime: use "today" and
        // overwrite just the time part. If parsing fails (empty
        // default or invalid text) fall back to 03:00.
        if (!string.IsNullOrWhiteSpace(raw) &&
            (TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ||
             TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, out t)))
        {
            return DateTime.Today.Add(t.ToTimeSpan());
        }
        return DateTime.Today.AddHours(3);
    }

    // ------------------ Layout helpers ------------------
    private static TableLayoutPanel BuildFormTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 8, 4, 4) };
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }
}
