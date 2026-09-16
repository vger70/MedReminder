using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Impostazioni suddivise per scheda:
//   Email  — SMTP host/porta/TLS/user, password protetta DPAPI, test invio.
//   Avvio  — auto-start con Windows.
//   Backup — export/import DB.
//
// I dati SMTP editabili vengono serializzati in
// %LOCALAPPDATA%\MedReminder\smtp.settings.json (aggiunto al chain di
// IConfiguration in Program.cs con reloadOnChange=true, così
// IOptionsMonitor<SmtpSettings> si aggiorna senza riavvio).
internal sealed class SettingsDialog : MedReminderFormBase
{
    private readonly IOptionsMonitor<SmtpSettings> _smtpMonitor;
    private readonly IOptionsMonitor<BackupSettings> _backupMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly IEmailNotificationService _emailService;
    private readonly IAutoStartService _autoStart;
    private readonly IBackupService _backup;
    private readonly IBackupStateStore _backupState;
    private readonly IApplicationRestarter _restarter;
    private readonly ILocalizationService _loc;

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

    // Component condiviso per i tooltip esplicativi sui campi tecnici
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
        IOptionsMonitor<BackupSettings> backupMonitor,
        ISmtpCredentialStore credentialStore,
        IEmailNotificationService emailService,
        IAutoStartService autoStart,
        IBackupService backup,
        IBackupStateStore backupState,
        IApplicationRestarter restarter,
        ILocalizationService localization)
    {
        _smtpMonitor = smtpMonitor;
        _backupMonitor = backupMonitor;
        _credentialStore = credentialStore;
        _emailService = emailService;
        _autoStart = autoStart;
        _backup = backup;
        _backupState = backupState;
        _restarter = restarter;
        _loc = localization;

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
        tabs.TabPages.Add(BuildEmailTab());
        tabs.TabPages.Add(BuildStartupTab());
        tabs.TabPages.Add(BuildBackupTab());

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
        // = DisplayName localizzato per la lingua corrente.
        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
        };
        foreach (var lang in SupportedLanguages.All)
        {
            var localizedName = lang.Code == "en"
                ? _loc.Get("Language.English")
                : _loc.Get("Language.Italian");
            _languageCombo.Items.Add(new LanguageChoice(lang.Code, localizedName));
            if (string.Equals(lang.Code, _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                _languageCombo.SelectedIndex = _languageCombo.Items.Count - 1;
            }
        }
        _languageCombo.DisplayMember = nameof(LanguageChoice.DisplayName);

        _tooltips.SetToolTip(_languageCombo, _loc.Get("Ui.SettingsDialog.Tooltip.Language"));

        var saveButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.General.Save"),
            AutoSize = true,
            Height = 30,
        };
        saveButton.Click += (_, _) => SaveLanguage();

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
        panel.Controls.Add(saveButton);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
    }

    private void SaveLanguage()
    {
        if (_languageCombo.SelectedItem is not LanguageChoice choice) return;

        try
        {
            WriteUserSettingsToDisk(new UserSettings { Language = choice.Code });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Se la lingua non è cambiata, nessun restart necessario —
        // basta un feedback breve. Diversamente chiediamo conferma
        // e riavviamo.
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

    // ------------------ Email tab ------------------
    private TabPage BuildEmailTab()
    {
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
        _toBox = new TextBox { Dock = DockStyle.Fill, Text = current.ToAddress };
        _timeoutBox = new NumericUpDown { Dock = DockStyle.Left, Width = 100, Minimum = 5, Maximum = 300, Value = current.TimeoutSeconds > 0 ? current.TimeoutSeconds : 30 };

        _tooltips.SetToolTip(_hostBox, _loc.Get("Ui.SettingsDialog.Tooltip.Host"));
        _tooltips.SetToolTip(_portBox, _loc.Get("Ui.SettingsDialog.Tooltip.Port"));
        _tooltips.SetToolTip(_useTlsBox, _loc.Get("Ui.SettingsDialog.Tooltip.UseTls"));
        _tooltips.SetToolTip(_usernameBox, _loc.Get("Ui.SettingsDialog.Tooltip.Username"));
        _tooltips.SetToolTip(_passwordBox, _loc.Get("Ui.SettingsDialog.Tooltip.Password"));
        _tooltips.SetToolTip(_clearPasswordBox, _loc.Get("Ui.SettingsDialog.Tooltip.ClearPassword"));
        _tooltips.SetToolTip(_fromBox, _loc.Get("Ui.SettingsDialog.Tooltip.From"));
        _tooltips.SetToolTip(_fromNameBox, _loc.Get("Ui.SettingsDialog.Tooltip.FromName"));
        _tooltips.SetToolTip(_toBox, _loc.Get("Ui.SettingsDialog.Tooltip.To"));
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
        AddRow(table, _loc.Get("Ui.SettingsDialog.Email.To"), _toBox);
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
                ToAddress = _toBox.Text.Trim(),
                TimeoutSeconds = (int)_timeoutBox.Value,
            };

            // Password: se l'utente ha digitato qualcosa la cifriamo;
            // altrimenti manteniamo quella corrente. Il checkbox "clear"
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
        // Prima salva perché il TestConnectionAsync lavora sui settings
        // correnti (IOptionsMonitor si aggiorna dopo la scrittura del file).
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
        // format), evita che l'utente cambi la data.
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
                // Crea la cartella se non esiste — feedback immediato all'utente.
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
            var file = await _backup.ExportAsync(directory, CancellationToken.None);
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
            var file = await _backup.ExportAsync(dialog.SelectedPath, CancellationToken.None);
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
        using var dialog = new OpenFileDialog
        {
            Title = _loc.Get("Ui.SettingsDialog.Backup.FileDialog.Title"),
            Filter = _loc.Get("Ui.SettingsDialog.Backup.FileDialog.Filter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.SettingsDialog.Backup.RestoreConfirm"),
            _loc.Get("Ui.SettingsDialog.Backup.RestoreConfirmTitle"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        button.Enabled = false;
        try
        {
            await _backup.ImportAsync(dialog.FileName, CancellationToken.None);
            MessageBox.Show(this,
                _loc.Get("Ui.SettingsDialog.Backup.RestoreDone"),
                _loc.Get("Ui.SettingsDialog.Backup.ImportTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _restarter.RestartAndExit();
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
        // Il DateTimePicker richiede un DateTime completo: usiamo la data
        // "oggi" e sovrascriviamo solo l'orario. Se il parsing fallisce
        // (default vuoto o testo invalido) fallback a 03:00.
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
