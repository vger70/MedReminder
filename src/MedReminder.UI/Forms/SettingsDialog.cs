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
        IApplicationRestarter restarter)
    {
        _smtpMonitor = smtpMonitor;
        _backupMonitor = backupMonitor;
        _credentialStore = credentialStore;
        _emailService = emailService;
        _autoStart = autoStart;
        _backup = backup;
        _backupState = backupState;
        _restarter = restarter;

        Text = "Impostazioni MedReminder";
        Width = 620;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildEmailTab());
        tabs.TabPages.Add(BuildStartupTab());
        tabs.TabPages.Add(BuildBackupTab());

        var closeButton = new Button { Text = "Chiudi", DialogResult = DialogResult.OK, Width = 100, Height = 32 };
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

    // ------------------ Email tab ------------------
    private TabPage BuildEmailTab()
    {
        var page = new TabPage("Email SMTP");
        var current = _smtpMonitor.CurrentValue;

        _hostBox = new TextBox { Dock = DockStyle.Fill, Text = current.Host };
        _portBox = new NumericUpDown { Dock = DockStyle.Left, Width = 100, Minimum = 1, Maximum = 65535, Value = current.Port > 0 ? current.Port : 587 };
        _useTlsBox = new CheckBox { Text = "Usa StartTLS", AutoSize = true, Checked = current.UseStartTls };
        _usernameBox = new TextBox { Dock = DockStyle.Fill, Text = current.Username };
        _passwordBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "(lascia vuoto per non modificare)" };
        _clearPasswordBox = new CheckBox { Text = "Rimuovi la password salvata", AutoSize = true };
        _fromBox = new TextBox { Dock = DockStyle.Fill, Text = current.FromAddress };
        _fromNameBox = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrEmpty(current.FromDisplayName) ? "MedReminder" : current.FromDisplayName };
        _toBox = new TextBox { Dock = DockStyle.Fill, Text = current.ToAddress };
        _timeoutBox = new NumericUpDown { Dock = DockStyle.Left, Width = 100, Minimum = 5, Maximum = 300, Value = current.TimeoutSeconds > 0 ? current.TimeoutSeconds : 30 };

        _tooltips.SetToolTip(_hostBox,
            "Server SMTP del tuo provider email.\nEsempi: smtp.gmail.com, smtp-mail.outlook.com, smtp.mail.yahoo.com");
        _tooltips.SetToolTip(_portBox,
            "Porta TCP del server SMTP.\n• 587: submission con StartTLS (raccomandata)\n• 465: SMTPS legacy\n• 25: outbound relay senza TLS (sconsigliata)");
        _tooltips.SetToolTip(_useTlsBox,
            "Attiva la cifratura StartTLS sulla connessione SMTP.\nRichiesta praticamente da ogni provider moderno.");
        _tooltips.SetToolTip(_usernameBox,
            "Nome utente di login SMTP. Di solito coincide con l'indirizzo email completo.");
        _tooltips.SetToolTip(_passwordBox,
            "Password / App Password per l'autenticazione SMTP.\n\nAttenzione: dal 2022 Gmail e Outlook.com richiedono un 'App Password' generato dalle impostazioni di sicurezza dell'account (2FA obbligatoria) — la password normale dell'account NON funziona.\n\nLa password viene cifrata con DPAPI (per utente Windows) e non è mai memorizzata in chiaro.");
        _tooltips.SetToolTip(_clearPasswordBox,
            "Se selezionato al salvataggio, rimuove la password DPAPI memorizzata. Utile per disaccoppiare l'app dall'account senza reinstallare.");
        _tooltips.SetToolTip(_fromBox,
            "Indirizzo mittente delle notifiche. Deve essere accettato dal server SMTP (di solito == username).");
        _tooltips.SetToolTip(_fromNameBox,
            "Nome visualizzato del mittente nel client email del destinatario.");
        _tooltips.SetToolTip(_toBox,
            "Indirizzo che riceverà le email di promemoria. Può essere lo stesso del mittente o un altro (es. inbox condivisa con un familiare).");
        _tooltips.SetToolTip(_timeoutBox,
            "Timeout in secondi per l'apertura e invio della singola email. 30 s è un valore sensato per SMTP domestico.");

        _passwordStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = _credentialStore.HasPassword ? System.Drawing.Color.DarkGreen : System.Drawing.Color.DarkGray,
            Text = _credentialStore.HasPassword ? "Password già memorizzata (DPAPI)." : "Nessuna password memorizzata.",
        };

        var testButton = new Button { Text = "Prova connessione", AutoSize = true, Height = 28 };
        var saveButton = new Button { Text = "Salva impostazioni SMTP", AutoSize = true, Height = 28 };
        testButton.Click += async (_, _) => await TestSmtpAsync(testButton);
        saveButton.Click += (_, _) => SaveSmtpSettings();

        var table = BuildFormTable();
        AddRow(table, "Host", _hostBox);
        AddRow(table, "Porta", _portBox);
        AddRow(table, string.Empty, _useTlsBox);
        AddRow(table, "Username", _usernameBox);
        AddRow(table, "Nuova password", _passwordBox);
        AddRow(table, string.Empty, _passwordStatusLabel);
        AddRow(table, string.Empty, _clearPasswordBox);
        AddRow(table, "Mittente (from)", _fromBox);
        AddRow(table, "Nome mittente", _fromNameBox);
        AddRow(table, "Destinatario (to)", _toBox);
        AddRow(table, "Timeout (s)", _timeoutBox);

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
            _passwordStatusLabel.Text = _credentialStore.HasPassword
                ? "Password già memorizzata (DPAPI)."
                : "Nessuna password memorizzata.";
            _passwordBox.Text = string.Empty;
            _clearPasswordBox.Checked = false;

            MessageBox.Show(this, "Impostazioni SMTP salvate.", "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore salvataggio", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var msg = ok ? "Connessione SMTP riuscita." : "Connessione SMTP fallita. Verifica configurazione e log.";
            var icon = ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(this, msg, "Test SMTP", MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Test SMTP", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        var page = new TabPage("Avvio automatico");

        _autoStartCheck = new CheckBox
        {
            Text = "Avvia MedReminder all'accesso a Windows (--minimized)",
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
                MessageBox.Show(this, ex.Message, "Errore auto-start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _autoStartCheck.Checked = _autoStart.IsEnabled;
            }
        };

        var note = new Label
        {
            AutoSize = true,
            Text = "L'avvio è per-utente (HKCU\\...\\Run). Non richiede privilegi amministrativi.",
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
        var page = new TabPage("Backup / Ripristino");
        var settings = _backupMonitor.CurrentValue;

        _dbPathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            Text = $"File database corrente:\n{_backup.DatabasePath}",
            ForeColor = System.Drawing.Color.DarkGray,
        };

        _backupEnabledBox = new CheckBox
        {
            Text = "Backup automatico giornaliero",
            AutoSize = true,
            Checked = settings.Enabled,
        };

        _backupDirectoryBox = new TextBox
        {
            Width = 400,
            Text = settings.Directory,
            ReadOnly = false,
        };
        var browseButton = new Button { Text = "Sfoglia…", AutoSize = true };
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

        _tooltips.SetToolTip(_backupEnabledBox,
            "Attiva un backup automatico giornaliero del database.\nSe il PC è spento all'orario preferito, il backup viene eseguito al primo avvio successivo del giorno.");
        _tooltips.SetToolTip(_backupDirectoryBox,
            "Cartella in cui vengono salvati i file di backup (medreminder-YYYYMMDD-HHmmss.db).\n\nSuggerimento: evita cartelle sincronizzate su cloud (OneDrive, Dropbox) a meno che tu voglia esplicitamente che i tuoi dati medici vengano copiati online.");
        _tooltips.SetToolTip(_backupTimePicker,
            "Orario preferito del backup giornaliero (24h locali).\n\nÈ un 'orientamento', non un tempo esatto: se il PC non è acceso a quell'ora, il backup verrà eseguito al primo avvio successivo del giorno.");
        _tooltips.SetToolTip(_backupRetentionBox,
            "Numero di giorni di conservazione dei backup automatici.\nFile più vecchi vengono cancellati automaticamente dopo un backup riuscito.\n\n0 = nessuna cancellazione automatica (sconsigliato: la cartella cresce all'infinito).");
        _tooltips.SetToolTip(browseButton,
            "Apri Esplora file per scegliere la cartella dei backup.");

        var saveButton = new Button { Text = "Salva impostazioni backup", AutoSize = true, Height = 30 };
        saveButton.Click += (_, _) => SaveBackupSettings();

        var runNowButton = new Button { Text = "Esegui backup adesso", AutoSize = true, Height = 30 };
        runNowButton.Click += async (_, _) => await RunBackupNowAsync(runNowButton);

        var exportButton = new Button { Text = "Esporta in cartella specifica…", AutoSize = true, Height = 30 };
        exportButton.Click += async (_, _) => await ExportBackupAsync(exportButton);

        var importButton = new Button { Text = "Ripristina backup…", AutoSize = true, Height = 30 };
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
        AddRow(table, "Cartella backup", directoryRow);
        AddRow(table, "Orario preferito", _backupTimePicker);
        AddRow(table, "Retention (giorni)", _backupRetentionBox);
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
            Text = "Il backup automatico richiede che MedReminder sia in esecuzione all'orario " +
                   "preferito. Se il PC è spento a quell'ora, il backup viene eseguito al primo " +
                   "avvio successivo del giorno.\n" +
                   "Il ripristino sovrascrive il DB corrente; una copia della versione precedente " +
                   "viene salvata come .bak-<timestamp>.",
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
            Description = "Seleziona la cartella dei backup",
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
                    "Per abilitare il backup automatico devi selezionare una cartella di destinazione.",
                    "Backup",
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
                        $"Impossibile creare la cartella:\n{ex.Message}",
                        "Backup",
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
                "Impostazioni backup salvate.",
                "OK",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore salvataggio backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunBackupNowAsync(Button button)
    {
        var directory = _backupDirectoryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show(this,
                "Seleziona prima una cartella di destinazione (e salva le impostazioni).",
                "Backup",
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

            var suffix = pruned > 0 ? $"\n{pruned} vecchi backup rimossi." : string.Empty;
            MessageBox.Show(this,
                $"Backup creato:\n{file}{suffix}",
                "Backup",
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
            MessageBox.Show(this, ex.Message, "Errore backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            button.Enabled = true;
        }
    }

    private async Task ExportBackupAsync(Button button)
    {
        using var dialog = new FolderBrowserDialog { Description = "Seleziona la cartella di destinazione" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        button.Enabled = false;
        try
        {
            var file = await _backup.ExportAsync(dialog.SelectedPath, CancellationToken.None);
            MessageBox.Show(this, $"Backup creato:\n{file}", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore export", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            Title = "Seleziona il file di backup",
            Filter = "Database SQLite (*.db)|*.db|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var confirm = MessageBox.Show(this,
            "L'operazione sovrascrive il database corrente. Al termine MedReminder verrà riavviato in automatico.\n\nProcedere?",
            "Conferma ripristino", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        button.Enabled = false;
        try
        {
            await _backup.ImportAsync(dialog.FileName, CancellationToken.None);
            var restartAnswer = MessageBox.Show(this,
                "Ripristino completato. Riavvio MedReminder ora per applicare le modifiche.",
                "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _ = restartAnswer;
            _restarter.RestartAndExit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Errore import", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _backupStatusLabel.Text = "Nessun backup eseguito finora.";
            return;
        }

        if (state.LastSuccessfulBackupAt is { } ok)
        {
            var okLocal = ok.ToLocalTime();
            var errorSuffix = state.LastError is not null
                ? $" · ultimo tentativo fallito: {state.LastError}"
                : string.Empty;
            _backupStatusLabel.ForeColor = state.LastError is null
                ? System.Drawing.Color.DarkGreen
                : System.Drawing.Color.DarkOrange;
            _backupStatusLabel.Text =
                $"Ultimo backup OK: {okLocal:dd/MM/yyyy HH:mm}{errorSuffix}";
            return;
        }

        _backupStatusLabel.ForeColor = System.Drawing.Color.Firebrick;
        _backupStatusLabel.Text = state.LastError is not null
            ? $"Ultimo tentativo fallito: {state.LastError}"
            : "Ultimo tentativo fallito.";
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
            _backupCloudWarningLabel.Text =
                "⚠ La cartella selezionata sembra un servizio cloud (OneDrive/Dropbox/Google Drive). " +
                "Il database (contiene nomi medicine, dosaggi, medico) verrà sincronizzato online. " +
                "Assicurati che sia quello che vuoi.";
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
