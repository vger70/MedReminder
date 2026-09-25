using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.UI.Forms;

// Modal "Export all data" dialog (C.3,
// docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §5.2). Collects the
// destination, a confirmed passphrase and the opt-in shared-settings
// checkboxes, then runs IExportService off the UI thread with a
// Progress<int> bar and a Cancel button.
//
// Admin profiles with more than one registered profile also get an
// "export every profile" switch: each profile is written to its own
// single-profile .mrz in a chosen folder, all under the same
// passphrase. The archive format is unchanged (one profile per file),
// and each file is restored by importing it from the profile it
// belongs to.
//
// Passphrase handling (§5.2, prompt Step 7): the passphrase text boxes
// are cleared and their buffers overwritten in Dispose; the passphrase
// is never logged and only crosses into the service as a char[] that
// the service owns for the duration of the call.
internal sealed class ExportDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly IExportService _exportService;
    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry _profileRegistry;

    private readonly Label _destinationLabel;
    private readonly TextBox _destinationBox;
    private readonly CheckBox _allProfilesBox;
    private readonly TextBox _passphraseBox;
    private readonly TextBox _passphraseConfirmBox;
    private readonly CheckBox _showPassphraseBox;
    private readonly CheckBox _includeSmtpSettingsBox;
    private readonly CheckBox _includeSmtpPasswordBox;
    private readonly CheckBox _includeBackupSettingsBox;
    private readonly CheckBox _includeUserSettingsBox;
    private readonly Label _smtpPasswordWarning;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _exportButton;
    private readonly Button _cancelButton;

    private CancellationTokenSource? _cts;
    private bool _running;

    public ExportDialog(
        ILocalizationService loc,
        IExportService exportService,
        ICurrentProfile currentProfile,
        IProfileRegistry profileRegistry)
    {
        _loc = loc;
        _exportService = exportService;
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;

        Text = _loc.Get("Ui.ExportDialog.Title");
        Width = 570;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Text = _loc.Get("Ui.ExportDialog.Intro"),
        };

        _allProfilesBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.AllProfiles"),
            Visible = _currentProfile.IsAdmin && _profileRegistry.ListProfiles().Count > 1,
        };
        var allProfilesHint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Color.DimGray,
            Text = _loc.Get("Ui.ExportDialog.AllProfiles.Hint"),
            Visible = false,
        };

        _destinationLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Destination.Label"),
        };
        _destinationBox = new TextBox { Width = 400, ReadOnly = true };
        _allProfilesBox.CheckedChanged += (_, _) =>
        {
            // The destination switches between a file and a folder.
            _destinationBox.Text = string.Empty;
            _destinationLabel.Text = _loc.Get(_allProfilesBox.Checked
                ? "Ui.ExportDialog.DestinationFolder.Label"
                : "Ui.ExportDialog.Destination.Label");
            allProfilesHint.Visible = _allProfilesBox.Checked;
        };
        var browseButton = new Button { Text = _loc.Get("Common.Browse"), AutoSize = true };
        browseButton.Click += (_, _) => BrowseDestination();
        var destinationRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        destinationRow.Controls.Add(_destinationBox);
        destinationRow.Controls.Add(browseButton);

        var passphraseLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Passphrase.Label"),
        };
        _passphraseBox = new TextBox { Width = 300, UseSystemPasswordChar = true };
        var passphraseConfirmLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.PassphraseConfirm.Label"),
        };
        _passphraseConfirmBox = new TextBox { Width = 300, UseSystemPasswordChar = true };
        _showPassphraseBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.ShowPassphrase"),
        };
        _showPassphraseBox.CheckedChanged += (_, _) =>
        {
            var hide = !_showPassphraseBox.Checked;
            _passphraseBox.UseSystemPasswordChar = hide;
            _passphraseConfirmBox.UseSystemPasswordChar = hide;
        };

        var lostPassphraseWarning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Color.DarkOrange,
            Text = _loc.Get("Ui.ExportDialog.Warning.LostPassphrase"),
        };

        _includeSmtpSettingsBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Include.SmtpTransport"),
        };
        _includeSmtpPasswordBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Include.SmtpPassword"),
            Enabled = false,
        };
        _smtpPasswordWarning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Color.DarkOrange,
            Text = _loc.Get("Ui.ExportDialog.Warning.SmtpPasswordIncluded"),
            Visible = false,
        };
        // The password opt-in only makes sense together with the SMTP
        // transport opt-in (§3.4): gate it on the transport checkbox.
        _includeSmtpSettingsBox.CheckedChanged += (_, _) =>
        {
            _includeSmtpPasswordBox.Enabled = _includeSmtpSettingsBox.Checked;
            if (!_includeSmtpSettingsBox.Checked)
            {
                _includeSmtpPasswordBox.Checked = false;
            }
        };
        _includeSmtpPasswordBox.CheckedChanged += (_, _) =>
            _smtpPasswordWarning.Visible = _includeSmtpPasswordBox.Checked;

        _includeBackupSettingsBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Include.BackupPrefs"),
        };
        _includeUserSettingsBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.Include.UserPrefs"),
        };

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
            MaximumSize = new Size(520, 0),
            Text = string.Empty,
        };

        _exportButton = new Button
        {
            Text = _loc.Get("Ui.ExportDialog.ExportButton"),
            AutoSize = true,
            Height = 30,
        };
        _exportButton.Click += async (_, _) => await RunExportAsync();
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
        buttons.Controls.Add(_exportButton);
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
        layout.Controls.Add(_allProfilesBox);
        layout.Controls.Add(allProfilesHint);
        layout.Controls.Add(_destinationLabel);
        layout.Controls.Add(destinationRow);
        layout.Controls.Add(passphraseLabel);
        layout.Controls.Add(_passphraseBox);
        layout.Controls.Add(passphraseConfirmLabel);
        layout.Controls.Add(_passphraseConfirmBox);
        layout.Controls.Add(_showPassphraseBox);
        layout.Controls.Add(lostPassphraseWarning);
        layout.Controls.Add(_includeSmtpSettingsBox);
        layout.Controls.Add(_includeSmtpPasswordBox);
        layout.Controls.Add(_smtpPasswordWarning);
        layout.Controls.Add(_includeBackupSettingsBox);
        layout.Controls.Add(_includeUserSettingsBox);
        layout.Controls.Add(_progressBar);
        layout.Controls.Add(_statusLabel);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        CancelButton = _cancelButton;
    }

    private void BrowseDestination()
    {
        if (_allProfilesBox.Checked)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = _loc.Get("Ui.ExportDialog.DestinationFolder.Label"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                _destinationBox.Text = folderDialog.SelectedPath;
            }
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = _loc.Get("Ui.ExportDialog.Destination.Label"),
            Filter = _loc.Get("Ui.ExportDialog.FileFilter"),
            DefaultExt = ExportFormat.ArchiveExtension.TrimStart('.'),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "medreminder-export" + ExportFormat.ArchiveExtension,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _destinationBox.Text = dialog.FileName;
        }
    }

    private async Task RunExportAsync()
    {
        if (_running) return;

        var allProfiles = _allProfilesBox.Checked;
        var destination = _destinationBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            ShowValidation(_loc.Get(allProfiles
                ? "Ui.ExportDialog.Error.NoDestinationFolder"
                : "Ui.ExportDialog.Error.NoDestination"));
            return;
        }

        var passphrase = _passphraseBox.Text;
        if (passphrase.Length < ExportFormat.MinPassphraseLength)
        {
            ShowValidation(_loc.Get(
                "Ui.Export.Error.PassphraseTooShort", ExportFormat.MinPassphraseLength));
            return;
        }
        if (!string.Equals(passphrase, _passphraseConfirmBox.Text, StringComparison.Ordinal))
        {
            ShowValidation(_loc.Get("Ui.ExportDialog.Error.PassphraseMismatch"));
            return;
        }

        var options = new ExportOptions
        {
            DestinationPath = destination,
            Scope = ExportScope.Profile,
            IncludeSmtpSettings = _includeSmtpSettingsBox.Checked,
            IncludeSmtpPassword = _includeSmtpPasswordBox.Checked,
            IncludeBackupSettings = _includeBackupSettingsBox.Checked,
            IncludeUserSettings = _includeUserSettingsBox.Checked,
        };

        var passphraseBuffer = passphrase.ToCharArray();
        _cts = new CancellationTokenSource();
        SetRunning(true);
        var progress = new Progress<int>(value => _progressBar.Value = Math.Clamp(value, 0, 100));

        try
        {
            string successMessage;
            if (allProfiles)
            {
                var count = await ExportEveryProfileAsync(
                    destination, options, passphraseBuffer, progress, _cts.Token);
                successMessage = _loc.Get("Ui.ExportDialog.SuccessAll", count, destination);
            }
            else
            {
                var path = await _exportService.ExportAsync(
                    options, passphraseBuffer, progress, _cts.Token);
                successMessage = _loc.Get("Ui.ExportDialog.Success", path);
            }

            MessageBox.Show(this,
                successMessage,
                _loc.Get("Ui.ExportDialog.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ProfileExportFailedException ex)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = _loc.Get(
                "Ui.ExportDialog.Error.ProfileFailed", ex.ProfileName, ex.InnerException?.Message ?? string.Empty);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = _loc.Get("Ui.ExportDialog.Cancelled");
        }
        catch (ExportValidationException ex) when (ex.Reason == ExportValidationReason.PassphraseTooShort)
        {
            ShowValidation(_loc.Get(
                "Ui.Export.Error.PassphraseTooShort", ExportFormat.MinPassphraseLength));
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            Array.Clear(passphraseBuffer);
            SetRunning(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    // Writes one single-profile archive per registered profile into
    // folder. Stops at the first failure; files already written are
    // kept. Returns the number of archives written.
    private async Task<int> ExportEveryProfileAsync(
        string folder,
        ExportOptions baseOptions,
        char[] passphrase,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var profiles = _profileRegistry.ListProfiles();
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            var index = i;
            var perProfile = new Progress<int>(value =>
                progress.Report(((index * 100) + value) / profiles.Count));
            var options = baseOptions with
            {
                DestinationPath = Path.Combine(
                    folder, $"medreminder-export-{profile.Id}-{timestamp}{ExportFormat.ArchiveExtension}"),
                ProfileId = profile.Id,
            };

            try
            {
                await _exportService.ExportAsync(options, passphrase, perProfile, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ExportValidationException)
            {
                throw new ProfileExportFailedException(profile.DisplayName, ex);
            }
        }

        return profiles.Count;
    }

    private sealed class ProfileExportFailedException(string profileName, Exception inner)
        : Exception(inner.Message, inner)
    {
        public string ProfileName { get; } = profileName;
    }

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
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = _loc.Get("Ui.ExportDialog.Running");
        }
        _exportButton.Enabled = !running;
    }

    private void ShowValidation(string message)
    {
        _statusLabel.ForeColor = Color.Firebrick;
        _statusLabel.Text = message;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Overwrite the passphrase text buffers before releasing the
            // controls (prompt Step 7 passphrase handling).
            ZeroTextBox(_passphraseBox);
            ZeroTextBox(_passphraseConfirmBox);
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
