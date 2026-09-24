using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.UI.Forms;

// Modal "Import from export" dialog (C.3,
// docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §5.3). Picks a source
// archive, reads its manifest (without decrypting) to populate an info
// panel, requires an explicit "I understand this overwrites" checkbox,
// then runs IImportService off the UI thread with a Progress<int> bar
// and a Cancel button. On success it offers the restart prompt (§4.2
// step 11); RestartRequested tells the caller whether to restart the
// active profile.
//
// Passphrase handling: the passphrase box is cleared and its buffer
// overwritten in Dispose; the passphrase is never logged.
internal sealed class ImportDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly IImportService _importService;

    private readonly TextBox _sourceBox;
    private readonly TextBox _passphraseBox;
    private readonly CheckBox _showPassphraseBox;
    private readonly Label _infoLabel;
    private readonly CheckBox _confirmOverwriteBox;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _importButton;
    private readonly Button _cancelButton;

    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _manifestLoaded;

    public ImportDialog(ILocalizationService loc, IImportService importService)
    {
        _loc = loc;
        _importService = importService;

        Text = _loc.Get("Ui.ImportDialog.Title");
        Width = 560;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(520, 0),
            Text = _loc.Get("Ui.ImportDialog.Intro"),
        };

        var sourceLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ImportDialog.Source.Label"),
        };
        _sourceBox = new TextBox { Width = 400, ReadOnly = true };
        var browseButton = new Button { Text = _loc.Get("Common.Browse"), AutoSize = true };
        browseButton.Click += (_, _) => BrowseSource();
        var sourceRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        sourceRow.Controls.Add(_sourceBox);
        sourceRow.Controls.Add(browseButton);

        _infoLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(520, 0),
            ForeColor = System.Drawing.Color.DarkGray,
            Text = _loc.Get("Ui.ImportDialog.NoFileSelected"),
        };

        var passphraseLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ImportDialog.Passphrase.Label"),
        };
        _passphraseBox = new TextBox { Width = 300, UseSystemPasswordChar = true };
        _showPassphraseBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.ShowPassphrase"),
        };
        _showPassphraseBox.CheckedChanged += (_, _) =>
            _passphraseBox.UseSystemPasswordChar = !_showPassphraseBox.Checked;

        _confirmOverwriteBox = new CheckBox
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(520, 0),
            Text = _loc.Get("Ui.ImportDialog.Confirm.Overwrite"),
        };
        _confirmOverwriteBox.CheckedChanged += (_, _) => UpdateImportEnabled();

        _progressBar = new ProgressBar
        {
            Width = 520,
            Minimum = 0,
            Maximum = 100,
            Visible = false,
        };
        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(520, 0),
            Text = string.Empty,
        };

        _importButton = new Button
        {
            Text = _loc.Get("Ui.ImportDialog.ImportButton"),
            AutoSize = true,
            Height = 30,
            Enabled = false,
        };
        _importButton.Click += async (_, _) => await RunImportAsync();
        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            AutoSize = true,
            Height = 30,
        };
        _cancelButton.Click += (_, _) => OnCancel();

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(_importButton);
        buttons.Controls.Add(_cancelButton);

        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
            AutoScroll = true,
        };
        layout.Controls.Add(intro);
        layout.Controls.Add(sourceLabel);
        layout.Controls.Add(sourceRow);
        layout.Controls.Add(_infoLabel);
        layout.Controls.Add(passphraseLabel);
        layout.Controls.Add(_passphraseBox);
        layout.Controls.Add(_showPassphraseBox);
        layout.Controls.Add(_confirmOverwriteBox);
        layout.Controls.Add(_progressBar);
        layout.Controls.Add(_statusLabel);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        CancelButton = _cancelButton;
    }

    // True when the import succeeded and the user accepted the restart
    // prompt; the caller (SettingsDialog) performs the actual restart.
    public bool RestartRequested { get; private set; }

    private async void BrowseSource()
    {
        using var dialog = new OpenFileDialog
        {
            Title = _loc.Get("Ui.ImportDialog.Source.Label"),
            Filter = _loc.Get("Ui.ExportDialog.FileFilter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _sourceBox.Text = dialog.FileName;
        await LoadManifestAsync(dialog.FileName);
    }

    private async Task LoadManifestAsync(string archivePath)
    {
        _manifestLoaded = false;
        UpdateImportEnabled();
        try
        {
            var manifest = await _importService.ReadManifestAsync(
                archivePath, CancellationToken.None);
            _infoLabel.ForeColor = System.Drawing.Color.DarkGray;
            _infoLabel.Text = FormatManifest(manifest);
            _manifestLoaded = true;
        }
        catch (ImportFailedException ex)
        {
            _infoLabel.ForeColor = System.Drawing.Color.Firebrick;
            _infoLabel.Text = MapError(ex);
        }
        catch (Exception ex)
        {
            _infoLabel.ForeColor = System.Drawing.Color.Firebrick;
            _infoLabel.Text = ex.Message;
        }
        UpdateImportEnabled();
    }

    private string FormatManifest(ExportManifest manifest)
    {
        var included = new List<string>();
        if (manifest.Includes.SmtpSettings) included.Add(_loc.Get("Ui.ExportDialog.Include.SmtpTransport"));
        if (manifest.Includes.SmtpCredential) included.Add(_loc.Get("Ui.ExportDialog.Include.SmtpPassword"));
        if (manifest.Includes.BackupSettings) included.Add(_loc.Get("Ui.ExportDialog.Include.BackupPrefs"));
        if (manifest.Includes.UserSettings) included.Add(_loc.Get("Ui.ExportDialog.Include.UserPrefs"));
        var includedText = included.Count > 0
            ? string.Join(", ", included)
            : _loc.Get("Ui.ImportDialog.Info.NoSharedFiles");

        var createdLocal = manifest.CreatedAtUtc.ToLocalTime()
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        return _loc.Get(
            "Ui.ImportDialog.Info",
            manifest.AppVersion,
            createdLocal,
            manifest.Scope,
            manifest.ProfileId ?? "—",
            includedText);
    }

    private async Task RunImportAsync()
    {
        if (_running) return;

        var source = _sourceBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(source) || !_manifestLoaded)
        {
            ShowValidation(_loc.Get("Ui.ImportDialog.Error.NoSource"));
            return;
        }
        if (!_confirmOverwriteBox.Checked)
        {
            ShowValidation(_loc.Get("Ui.ImportDialog.Error.NotConfirmed"));
            return;
        }

        var passphraseBuffer = _passphraseBox.Text.ToCharArray();
        _cts = new CancellationTokenSource();
        SetRunning(true);
        var progress = new Progress<int>(value => _progressBar.Value = Math.Clamp(value, 0, 100));

        try
        {
            await _importService.ImportAsync(
                source, passphraseBuffer, new ImportOptions(), progress, _cts.Token);

            // §4.2 step 11: offer a restart so the hosted services pick
            // up the swapped DB cleanly.
            var restart = MessageBox.Show(this,
                _loc.Get("Ui.ImportDialog.Success.RestartPrompt"),
                _loc.Get("Ui.ImportDialog.Title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            RestartRequested = restart == DialogResult.Yes;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = _loc.Get("Ui.ImportDialog.Cancelled");
        }
        catch (ImportFailedException ex)
        {
            ShowValidation(MapError(ex));
        }
        catch (Exception ex)
        {
            ShowValidation(ex.Message);
        }
        finally
        {
            Array.Clear(passphraseBuffer);
            SetRunning(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private string MapError(ImportFailedException ex) => ex.Reason switch
    {
        ImportFailureReason.WrongPassphrase => _loc.Get("Ui.Import.Error.WrongPassphrase"),
        ImportFailureReason.Corrupt => _loc.Get("Ui.Import.Error.Corrupt"),
        ImportFailureReason.UnsupportedVersion => _loc.Get("Ui.Import.Error.UnsupportedVersion"),
        _ => ex.Message,
    };

    private void OnCancel()
    {
        if (_running)
        {
            _cts?.Cancel();
            return;
        }
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _progressBar.Visible = running;
        if (running)
        {
            _progressBar.Value = 0;
            _statusLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            _statusLabel.Text = _loc.Get("Ui.ImportDialog.Running");
        }
        UpdateImportEnabled();
    }

    private void UpdateImportEnabled()
        => _importButton.Enabled =
            !_running && _manifestLoaded && _confirmOverwriteBox.Checked;

    private void ShowValidation(string message)
    {
        _statusLabel.ForeColor = System.Drawing.Color.Firebrick;
        _statusLabel.Text = message;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ZeroTextBox(_passphraseBox);
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void ZeroTextBox(TextBox box)
    {
        if (box.IsDisposed) return;
        var length = box.Text.Length;
        box.Text = length > 0 ? new string('\0', length) : string.Empty;
        box.Clear();
    }
}
