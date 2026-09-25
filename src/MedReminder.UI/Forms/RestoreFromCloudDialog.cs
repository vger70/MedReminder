using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;

namespace MedReminder.UI.Forms;

// Modal "Restore from cloud folder" dialog (C.3+,
// docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §5.2). Enumerates the
// .mrz snapshots in the configured cloud folder (any folder the user
// picks, defaults to BackupSettings.CloudFolderDirectory), shows them
// with the metadata their manifests expose (date, hashed device,
// profile), asks the user to acknowledge the overwrite, then delegates
// to ICloudRestoreService — which itself delegates to IImportService,
// reusing the same overwrite semantics the C.3 Import dialog uses.
//
// Passphrase handling mirrors ImportDialog: cleared and zeroed in
// Dispose; the passphrase is never logged.
internal sealed class RestoreFromCloudDialog : MedReminderFormBase
{
    private readonly ILocalizationService _loc;
    private readonly ICloudRestoreService _cloudRestore;
    private readonly ICloudBackupPassphraseStore _passStore;
    private readonly string _defaultFolder;

    private readonly TextBox _folderBox;
    private readonly ListView _snapshotList;
    private readonly TextBox _passphraseBox;
    private readonly CheckBox _useSavedPassphraseBox;
    private readonly CheckBox _showPassphraseBox;
    private readonly CheckBox _confirmOverwriteBox;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _restoreButton;
    private readonly Button _cancelButton;

    private CancellationTokenSource? _cts;
    private bool _running;
    private IReadOnlyList<CloudSnapshotInfo> _snapshots = Array.Empty<CloudSnapshotInfo>();

    public bool RestartRequested { get; private set; }

    public RestoreFromCloudDialog(
        ILocalizationService loc,
        ICloudRestoreService cloudRestore,
        ICloudBackupPassphraseStore passStore,
        string defaultFolder)
    {
        _loc = loc;
        _cloudRestore = cloudRestore;
        _passStore = passStore;
        _defaultFolder = defaultFolder ?? string.Empty;

        Text = _loc.Get("Ui.RestoreCloudDialog.Title");
        Width = 640;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            Text = _loc.Get("Ui.RestoreCloudDialog.Intro"),
        };

        var folderLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.RestoreCloudDialog.Folder.Label"),
        };
        _folderBox = new TextBox { Width = 460, Text = _defaultFolder };
        var browseButton = new Button
        {
            Text = _loc.Get("Common.Browse"),
            AutoSize = true,
        };
        browseButton.Click += (_, _) => BrowseFolder();
        var refreshButton = new Button
        {
            Text = _loc.Get("Ui.RestoreCloudDialog.Refresh"),
            AutoSize = true,
        };
        refreshButton.Click += async (_, _) => await ReloadSnapshotsAsync();
        var folderRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        folderRow.Controls.Add(_folderBox);
        folderRow.Controls.Add(browseButton);
        folderRow.Controls.Add(refreshButton);

        _snapshotList = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            Width = 600,
            Height = 220,
            GridLines = true,
        };
        _snapshotList.Columns.Add(_loc.Get("Ui.RestoreCloudDialog.Snapshot.Column.Date"), 160);
        _snapshotList.Columns.Add(_loc.Get("Ui.RestoreCloudDialog.Snapshot.Column.Profile"), 200);
        _snapshotList.Columns.Add(_loc.Get("Ui.RestoreCloudDialog.Snapshot.Column.Device"), 120);
        _snapshotList.Columns.Add(_loc.Get("Ui.RestoreCloudDialog.Snapshot.Column.Source"), 90);

        var passphraseLabel = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.RestoreCloudDialog.Passphrase.Label"),
        };
        _passphraseBox = new TextBox
        {
            Width = 300,
            UseSystemPasswordChar = true,
        };
        _showPassphraseBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.ExportDialog.ShowPassphrase"),
        };
        _showPassphraseBox.CheckedChanged += (_, _) =>
            _passphraseBox.UseSystemPasswordChar = !_showPassphraseBox.Checked;

        _useSavedPassphraseBox = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.RestoreCloudDialog.Passphrase.UseSaved"),
            Checked = _passStore.HasPassphrase,
            Enabled = _passStore.HasPassphrase,
        };
        _useSavedPassphraseBox.CheckedChanged += (_, _) =>
        {
            _passphraseBox.Enabled = !_useSavedPassphraseBox.Checked;
            _showPassphraseBox.Enabled = !_useSavedPassphraseBox.Checked;
        };
        _passphraseBox.Enabled = !_useSavedPassphraseBox.Checked;
        _showPassphraseBox.Enabled = !_useSavedPassphraseBox.Checked;

        var passphraseRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
        };
        passphraseRow.Controls.Add(_passphraseBox);
        passphraseRow.Controls.Add(_showPassphraseBox);

        _confirmOverwriteBox = new CheckBox
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            Text = _loc.Get("Ui.RestoreCloudDialog.Confirm.Overwrite"),
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            ForeColor = System.Drawing.Color.DarkGray,
        };

        _progressBar = new ProgressBar
        {
            Width = 600,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };

        _restoreButton = new Button
        {
            Text = _loc.Get("Ui.RestoreCloudDialog.Restore.Button"),
            AutoSize = true,
            Height = 30,
        };
        _restoreButton.Click += async (_, _) => await RunRestoreAsync();

        _cancelButton = new Button
        {
            Text = _loc.Get("Common.Cancel"),
            AutoSize = true,
            Height = 30,
        };
        _cancelButton.Click += (_, _) =>
        {
            if (_running)
            {
                _cts?.Cancel();
            }
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
        };
        actions.Controls.Add(_restoreButton);
        actions.Controls.Add(_cancelButton);

        var container = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            WrapContents = false,
            AutoScroll = true,
        };
        container.Controls.Add(intro);
        container.Controls.Add(folderLabel);
        container.Controls.Add(folderRow);
        container.Controls.Add(_snapshotList);
        container.Controls.Add(_useSavedPassphraseBox);
        container.Controls.Add(passphraseLabel);
        container.Controls.Add(passphraseRow);
        container.Controls.Add(_confirmOverwriteBox);
        container.Controls.Add(_progressBar);
        container.Controls.Add(_statusLabel);
        container.Controls.Add(actions);

        Controls.Add(container);
        CancelButton = _cancelButton;

        Shown += async (_, _) => await ReloadSnapshotsAsync();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.RestoreCloudDialog.Folder.Browse"),
            InitialDirectory = string.IsNullOrWhiteSpace(_folderBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _folderBox.Text,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dialog.SelectedPath;
            _ = ReloadSnapshotsAsync();
        }
    }

    private async Task ReloadSnapshotsAsync()
    {
        _snapshotList.Items.Clear();
        _snapshots = Array.Empty<CloudSnapshotInfo>();
        var folder = _folderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.NoFolder");
            return;
        }

        _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Loading");
        try
        {
            _snapshots = await _cloudRestore.ListSnapshotsAsync(folder, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Error", ex.Message);
            return;
        }

        if (_snapshots.Count == 0)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Empty");
            return;
        }

        foreach (var s in _snapshots)
        {
            var item = new ListViewItem(new[]
            {
                s.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                s.ProfileId,
                s.DeviceHostHash.Length >= 12 ? s.DeviceHostHash[..12] : s.DeviceHostHash,
                string.IsNullOrEmpty(s.Source) ? "user" : s.Source,
            })
            {
                Tag = s,
            };
            _snapshotList.Items.Add(item);
        }

        if (_snapshotList.Items.Count > 0)
        {
            _snapshotList.Items[0].Selected = true;
        }
        _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Ready", _snapshots.Count);
    }

    private async Task RunRestoreAsync()
    {
        if (_running) return;

        if (_snapshotList.SelectedItems.Count == 0
            || _snapshotList.SelectedItems[0].Tag is not CloudSnapshotInfo selected)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.SelectSnapshot");
            return;
        }

        if (!_confirmOverwriteBox.Checked)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Confirm.Overwrite.Required");
            return;
        }

        char[]? passphrase = null;
        try
        {
            if (_useSavedPassphraseBox.Checked)
            {
                passphrase = _passStore.GetPassphrase();
                if (passphrase is null || passphrase.Length == 0)
                {
                    _statusLabel.Text = _loc.Get("Ui.CloudBackup.Error.PassphraseMissing");
                    return;
                }
            }
            else
            {
                var typed = _passphraseBox.Text ?? string.Empty;
                if (typed.Length == 0)
                {
                    _statusLabel.Text = _loc.Get("Ui.CloudBackup.Error.PassphraseMissing");
                    return;
                }
                passphrase = typed.ToCharArray();
            }

            _running = true;
            _cts = new CancellationTokenSource();
            _restoreButton.Enabled = false;
            _progressBar.Value = 0;
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Running");

            var progress = new Progress<int>(p =>
            {
                if (!IsDisposed && !_progressBar.IsDisposed)
                {
                    _progressBar.Value = Math.Clamp(p, 0, 100);
                }
            });

            await _cloudRestore.RestoreAsync(
                selected.ArchivePath,
                passphrase,
                new ImportOptions(),
                progress,
                _cts.Token);

            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Restore.Success");
            var restart = MessageBox.Show(
                this,
                _loc.Get("Ui.RestoreCloudDialog.RestartPrompt"),
                _loc.Get("Ui.RestoreCloudDialog.RestartPrompt.Title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            RestartRequested = restart == DialogResult.Yes;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Cancelled");
        }
        catch (ImportFailedException ex)
        {
            _statusLabel.Text = ex.Reason switch
            {
                ImportFailureReason.WrongPassphrase =>
                    _loc.Get("Ui.Import.Error.WrongPassphrase"),
                ImportFailureReason.UnsupportedVersion =>
                    _loc.Get("Ui.Import.Error.UnsupportedVersion"),
                _ => _loc.Get("Ui.Import.Error.Corrupt"),
            };
        }
        catch (Exception ex)
        {
            _statusLabel.Text = _loc.Get("Ui.RestoreCloudDialog.Status.Error", ex.Message);
        }
        finally
        {
            if (passphrase is not null)
            {
                Array.Clear(passphrase, 0, passphrase.Length);
            }
            _running = false;
            _cts?.Dispose();
            _cts = null;
            _restoreButton.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var text = _passphraseBox.Text;
            if (!string.IsNullOrEmpty(text))
            {
                // Overwrite the textbox buffer before disposing so a
                // memory scrape after the dialog closes cannot recover
                // the passphrase.
                _passphraseBox.Text = new string('\0', text.Length);
                _passphraseBox.Text = string.Empty;
            }
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
