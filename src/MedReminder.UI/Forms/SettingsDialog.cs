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
internal sealed class SettingsDialog : Form
{
    private readonly IOptionsMonitor<SmtpSettings> _smtpMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly IEmailNotificationService _emailService;
    private readonly IAutoStartService _autoStart;
    private readonly IBackupService _backup;

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

    public SettingsDialog(
        IOptionsMonitor<SmtpSettings> smtpMonitor,
        ISmtpCredentialStore credentialStore,
        IEmailNotificationService emailService,
        IAutoStartService autoStart,
        IBackupService backup)
    {
        _smtpMonitor = smtpMonitor;
        _credentialStore = credentialStore;
        _emailService = emailService;
        _autoStart = autoStart;
        _backup = backup;

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

        _dbPathLabel = new Label
        {
            AutoSize = true,
            Text = $"File database corrente: {_backup.DatabasePath}",
        };

        var exportButton = new Button { Text = "Esporta backup…", AutoSize = true, Height = 32 };
        exportButton.Click += async (_, _) => await ExportBackupAsync(exportButton);

        var importButton = new Button { Text = "Ripristina backup…", AutoSize = true, Height = 32 };
        importButton.Click += async (_, _) => await ImportBackupAsync(importButton);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            AutoEllipsis = false,
            Text = "Il ripristino sovrascrive il DB corrente. Un file di backup della versione " +
                   "precedente viene creato in automatico prima della sostituzione. È consigliato " +
                   "chiudere l'applicazione e riavviarla dopo un ripristino per evitare inconsistenze.",
            ForeColor = System.Drawing.Color.DarkGray,
        };

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
        };
        panel.Controls.Add(_dbPathLabel);
        panel.Controls.Add(exportButton);
        panel.Controls.Add(importButton);
        panel.Controls.Add(note);
        page.Controls.Add(panel);
        return page;
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
            "L'operazione sovrascrive il database corrente. È fortemente consigliato riavviare l'applicazione al termine.\n\nProcedere?",
            "Conferma ripristino", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        button.Enabled = false;
        try
        {
            await _backup.ImportAsync(dialog.FileName, CancellationToken.None);
            MessageBox.Show(this, "Backup ripristinato. Riavvia MedReminder.", "Import",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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
