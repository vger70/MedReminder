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

// Settings → Backup (the section list and the dialog frame are in
// SettingsDialog.cs).
internal sealed partial class SettingsDialog
{
    // Backup section.
    private Panel BuildBackupTab()
    {
        var page = new Panel();
        var isAdmin = _currentProfile.IsAdmin;
        var settings = _backupMonitor.CurrentValue;

        _dbPathLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(560, 0),
            Text = _loc.Get("Ui.SettingsDialog.Backup.DbPath", _backup.DatabasePath),
            ForeColor = UiColors.Hint,
        };

        _backupEnabledBox = new CheckBox
        {
            Text = _loc.Get("Ui.SettingsDialog.Backup.Enable"),
            AutoSize = true,
            Checked = settings.Enabled,
        };

        _backupDirectoryBox = new TextBox
        {
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
            Value = ParsePreferredTimeAsDateTime(settings.PreferredTime)
        };

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
        saveButton.Click += async (_, _) => await SaveBackupSettingsAsync();

        var runNowButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.RunNow"), AutoSize = true, Height = 30 };
        runNowButton.Click += async (_, _) => await RunBackupNowAsync(runNowButton);

        var exportButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.ExportCustom"), AutoSize = true, Height = 30 };
        exportButton.Click += async (_, _) => await ExportBackupAsync(exportButton);

        var importButton = new Button { Text = _loc.Get("Ui.SettingsDialog.Backup.Restore"), AutoSize = true, Height = 30 };
        importButton.Click += async (_, _) => await ImportBackupAsync(importButton);

        // C.3: encrypted, portable export / import (§5.4). Sits next to
        // the raw DB backup / restore because it is the same "move my
        // data" concern, but produces a passphrase-encrypted .mrz that
        // is portable across Windows accounts and machines.
        var exportEncryptedButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.File.ExportData"),
            AutoSize = true,
            Height = 30,
        };
        exportEncryptedButton.Click += (_, _) => ShowExportDialog();

        var importEncryptedButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.File.ImportData"),
            AutoSize = true,
            Height = 30,
        };
        importEncryptedButton.Click += async (_, _) => await ShowImportDialog();

        _backupStatusLabel = new Label { AutoSize = true };
        UpdateBackupStatusLabel();

        _backupCloudWarningLabel = new Label
        {
            AutoSize = true,
            // Constrained to the width available in table column 1
            // (dialog − 160 − container padding − table padding),
            // so the localized warning text wraps within the tab
            // instead of forcing an horizontal scrollbar.
            MaximumSize = new System.Drawing.Size(540, 0),
            ForeColor = UiColors.Warning,
            Text = string.Empty,
            Visible = false,
        };
        UpdateCloudWarning();
        _backupDirectoryBox.TextChanged += (_, _) => UpdateCloudWarning();

        var directoryRow = BuildPathRow(_backupDirectoryBox, browseButton);

        var table = BuildFormTable();
        AddRow(table, string.Empty, _backupEnabledBox);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.Directory"), directoryRow);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.PreferredTime"), _backupTimePicker);
        AddRow(table, _loc.Get("Ui.SettingsDialog.Backup.RetentionDays"), _backupRetentionBox);
        AddRow(table, string.Empty, _backupStatusLabel);
        AddRow(table, string.Empty, _backupCloudWarningLabel);
        table.Visible = isAdmin;

        var actionButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 8),
        };
        if (isAdmin)
        {
            actionButtons.Controls.Add(saveButton);
            actionButtons.Controls.Add(runNowButton);
        }
        actionButtons.Controls.Add(exportButton);
        actionButtons.Controls.Add(importButton);
        actionButtons.Controls.Add(exportEncryptedButton);
        actionButtons.Controls.Add(importEncryptedButton);

        // C.3+ (§5.3): Restore-from-cloud sits next to the encrypted
        // export / import buttons since it is the same "move my data"
        // concern. Always visible so a non-admin profile can still
        // restore a snapshot into its own DB after installing the app
        // on a fresh machine.
        var restoreFromCloudButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.File.RestoreFromCloud"),
            AutoSize = true,
            Height = 30,
        };
        restoreFromCloudButton.Click += async (_, _) => await ShowRestoreFromCloudDialog();
        actionButtons.Controls.Add(restoreFromCloudButton);

        var cloudSection = BuildCloudBackupSection(isAdmin);

        var note = new Label
        {
            AutoSize = true,
            // Wraps against the tab's usable width (dialog width
            // minus container padding on both sides).
            MaximumSize = new System.Drawing.Size(700, 0),
            AutoEllipsis = false,
            Text = _loc.Get("Ui.SettingsDialog.Backup.Note"),
            ForeColor = UiColors.Hint,
        };

        // One column as wide as the section: the form tables stretch
        // with it, so the folder fields keep their Browse buttons in
        // view, and the section panel scrolls when the content is
        // taller than the window (admin view at 150 % and Large text).
        var container = BuildSectionColumn(new Padding(16));
        AddToColumn(container, _dbPathLabel);
        AddToColumn(container, table);
        if (cloudSection is not null)
        {
            AddToColumn(container, cloudSection);
        }
        actionButtons.Dock = DockStyle.Top;
        AddToColumn(container, actionButtons);
        AddToColumn(container, note);
        page.Controls.Add(container);
        return page;
    }

    // A single full-width column that grows to its content, docked at
    // the top of a scrolling section panel.
    private static TableLayoutPanel BuildSectionColumn(Padding padding)
    {
        var column = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = padding,
            Margin = Padding.Empty,
        };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return column;
    }

    private static void AddToColumn(TableLayoutPanel column, Control control)
    {
        column.RowCount++;
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.Controls.Add(control, 0, column.RowCount - 1);
    }

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §5.1):
    // second target that writes encrypted .mrz snapshots into a
    // user-picked cloud-synced folder. Admin-only writer (the settings
    // live in backup.settings.json alongside the raw-DB target) — a
    // non-admin profile can still trigger a restore from the encrypted
    // export / import row above.
    private Control? BuildCloudBackupSection(bool isAdmin)
    {
        if (!isAdmin)
        {
            // Nothing to configure for a non-admin profile — the
            // Restore-from-cloud button above remains available so the
            // user can still consume snapshots.
            return null;
        }

        var settings = _backupMonitor.CurrentValue;

        var groupTitle = new Label
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"),
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Padding = new Padding(0, 12, 0, 4),
        };

        _cloudEnabledBox = new CheckBox
        {
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Enabled"),
            AutoSize = true,
            Checked = settings.CloudFolderEnabled,
        };

        _cloudDirectoryBox = new TextBox
        {
            Text = settings.CloudFolderDirectory,
        };
        var cloudBrowseButton = new Button
        {
            Text = _loc.Get("Common.Browse"),
            AutoSize = true,
        };
        cloudBrowseButton.Click += (_, _) => BrowseCloudDirectory();
        _cloudBrowseButton = cloudBrowseButton;
        var cloudDirectoryRow = BuildPathRow(_cloudDirectoryBox, cloudBrowseButton);

        _cloudRetentionBox = new NumericUpDown
        {
            Width = 80,
            Minimum = 0,
            Maximum = 3650,
            Value = settings.CloudFolderRetention > 0 ? settings.CloudFolderRetention : 30,
        };

        _cloudPassStatusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(360, 0),
            Text = _cloudPassStore.HasPassphrase
                ? _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.Set")
                : _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.NotSet"),
        };
        _cloudPassChangeButton = new Button
        {
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.Change"),
            AutoSize = true,
        };
        _cloudPassChangeButton.Click += (_, _) => ChangeCloudPassphrase();
        var passRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            // Wraps the button under the text when the section is
            // narrow instead of cutting the text.
            WrapContents = true,
            Dock = DockStyle.Fill,
        };
        passRow.Controls.Add(_cloudPassStatusLabel);
        passRow.Controls.Add(_cloudPassChangeButton);

        var notSyncWarning = _cloudNotSyncWarning = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            ForeColor = UiColors.Warning,
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Warning.NotSync"),
        };
        var lostPassWarning = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(600, 0),
            ForeColor = UiColors.Warning,
            Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Warning.LostPassphrase"),
        };

        var cloudTable = BuildFormTable();
        AddRow(cloudTable, string.Empty, _cloudEnabledBox);
        // The saved provider stays a choice even when this build does not
        // offer it (no client id), so a save does not switch it silently.
        var providers = new[] { CloudProvider.OneDrive, CloudProvider.GoogleDrive }
            .Where(p => _cloudAccounts?.IsAvailable(p) == true || p == settings.CloudProvider).ToList();
        if (providers.Count > 0)
        {
            if (settings.CloudProvider is { } saved && !string.IsNullOrEmpty(settings.CloudAccountId))
            {
                _cloudAccountIds[saved] = settings.CloudAccountId;
            }
            _cloudProviderChoices = [null, .. providers.Cast<CloudProvider?>()];
            _cloudProviderBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
            foreach (var choice in _cloudProviderChoices)
            {
                _cloudProviderBox.Items.Add(_loc.Get($"Ui.SettingsDialog.CloudBackup.Provider.{choice?.ToString() ?? "Folder"}"));
            }
            _cloudProviderBox.SelectedIndex = Math.Max(0, _cloudProviderChoices.IndexOf(settings.CloudProvider));
            _cloudProviderBox.SelectedIndexChanged += async (_, _) =>
            {
                UpdateCloudTargetControls();
                await RefreshCloudAccountAsync();
            };
            _cloudAccountLabel = new Label { AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
            _cloudSignInButton = new Button { Text = _loc.Get("Ui.SettingsDialog.CloudBackup.SignIn"), AutoSize = true };
            _cloudSignInButton.Click += async (_, _) => await SignInCloudBackupAsync();
            var accountRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                // Wraps the button under the text when the section is
                // narrow instead of cutting the text.
                WrapContents = true,
                Dock = DockStyle.Fill,
            };
            accountRow.Controls.Add(_cloudAccountLabel);
            accountRow.Controls.Add(_cloudSignInButton);
            AddRow(cloudTable, _loc.Get("Ui.SettingsDialog.CloudBackup.Provider.Label"), _cloudProviderBox);
            AddRow(cloudTable, _loc.Get("Ui.SettingsDialog.CloudBackup.Account.Label"), accountRow);
            _ = RefreshCloudAccountAsync();
        }
        AddRow(cloudTable, _loc.Get("Ui.SettingsDialog.CloudBackup.Directory.Label"), cloudDirectoryRow);
        AddRow(cloudTable, _loc.Get("Ui.SettingsDialog.CloudBackup.Retention.Label"), _cloudRetentionBox);
        AddRow(cloudTable, _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.Label"), passRow);
        AddRow(cloudTable, string.Empty, notSyncWarning);
        AddRow(cloudTable, string.Empty, lostPassWarning);

        var wrapper = BuildSectionColumn(Padding.Empty);
        wrapper.Dock = DockStyle.Fill;
        AddToColumn(wrapper, groupTitle);
        AddToColumn(wrapper, cloudTable);
        UpdateCloudTargetControls();
        return wrapper;
    }

    // Without the provider choice (no app registration, or a non-admin
    // view) the saved target applies, so validation and the folder row
    // follow what the save keeps.
    private CloudProvider? CloudTargetProvider => _cloudProviderBox is null
        ? _backupMonitor.CurrentValue.CloudProvider
        : _cloudProviderChoices[_cloudProviderBox.SelectedIndex];

    private string CloudTargetAccountId => _cloudProviderBox is null
        ? _backupMonitor.CurrentValue.CloudAccountId
        : CloudTargetProvider is { } provider ? _cloudAccountIds.GetValueOrDefault(provider, string.Empty) : string.Empty;

    private void UpdateCloudTargetControls()
    {
        var cloud = CloudTargetProvider is not null;
        _cloudDirectoryBox.Enabled = !cloud;
        if (_cloudBrowseButton is not null) _cloudBrowseButton.Enabled = !cloud;
        if (_cloudNotSyncWarning is not null) _cloudNotSyncWarning.Visible = !cloud;
        if (_cloudAccountLabel is not null) _cloudAccountLabel.Enabled = cloud;
        if (_cloudSignInButton is not null) _cloudSignInButton.Enabled = cloud;
    }

    private async Task RefreshCloudAccountAsync()
    {
        if (_cloudAccountLabel is null || _cloudAccounts is null) return;
        CloudAccount? account = null;
        if (CloudTargetProvider is { } provider && CloudTargetAccountId is { Length: > 0 } accountId)
        {
            try
            {
                account = await _cloudAccounts.FindAsync(provider, accountId, CancellationToken.None);
            }
            catch (Exception)
            {
                // Shown as not signed in.
            }
        }
        _cloudAccountLabel.Text = account?.UserName ?? _loc.Get("Ui.SettingsDialog.CloudBackup.Account.None");
    }

    private async Task SignInCloudBackupAsync()
    {
        if (_cloudAccounts is null || _cloudSignInButton is null || CloudTargetProvider is not { } provider) return;
        _cloudSignInButton.Enabled = false;
        UseWaitCursor = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var known = CloudTargetAccountId;
            var account = await _cloudAccounts.SignInAsync(provider,
                string.IsNullOrEmpty(known) ? null : known, timeout.Token);
            _cloudAccountIds[provider] = account.Id;
            await RefreshCloudAccountAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, _loc.Get(provider == CloudProvider.GoogleDrive
                    ? "Ui.SyncDialog.SignIn.Failed.GoogleDrive"
                    : "Ui.SyncDialog.SignIn.Failed", ex.Message),
                _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            _cloudSignInButton.Enabled = CloudTargetProvider is not null;
        }
    }

    private void BrowseCloudDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.SettingsDialog.CloudBackup.Directory.Browse.Title"),
            InitialDirectory = string.IsNullOrWhiteSpace(_cloudDirectoryBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : _cloudDirectoryBox.Text,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cloudDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void ChangeCloudPassphrase()
    {
        using var dialog = new ChangeCloudPassphraseDialog(_loc, _cloudPassStore);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cloudPassStatusLabel.Text = _loc.Get("Ui.SettingsDialog.CloudBackup.Passphrase.Set");
        }
    }

    // B.1 Phase 3d (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.7): an
    // import or a restore on a synced profile starts a new sync
    // generation; the other devices discard what they have not sent yet
    // and rebuild. The user confirms, then the sync publishes what is
    // pending and stops until the restart. False when the user declines.
    private async Task<bool> ConfirmSyncResetAsync(string profileId)
    {
        var synced = File.Exists(Path.Combine(AppDataPaths.GetProfileDataDirectory(profileId), "sync.settings.json"));
        if (!synced) return true;
        if (MessageBox.Show(this, _loc.Get("Ui.SyncDialog.ResetWarning"), _loc.Get("Common.Warning"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return false;
        }
        if (_sync is not null && string.Equals(profileId, _currentProfile.Id, StringComparison.Ordinal))
        {
            try
            {
                await _sync.SuspendAsync();
            }
            catch (Exception)
            {
                // Offline folder: what was not sent is discarded, as the
                // warning says.
            }
        }
        return true;
    }

    private async Task ShowRestoreFromCloudDialog()
    {
        if (!await ConfirmSyncResetAsync(_currentProfile.Id)) return;
        var defaultFolder = _cloudDirectoryBox is null
            ? _backupMonitor.CurrentValue.CloudFolderDirectory
            : _cloudDirectoryBox.Text;
        // The saved target decides: the storage reads the saved settings.
        var storage = _backupMonitor.CurrentValue.CloudProvider is not null ? _archiveStorage : null;
        using var dialog = new RestoreFromCloudDialog(
            _loc, _cloudRestore, _cloudPassStore, _currentProfile, _profileRegistry,
            defaultFolder ?? string.Empty, storage, _backupMonitor.CurrentValue.CloudProvider ?? CloudProvider.OneDrive);
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.OK && dialog.RestartRequested)
        {
            _restarter.RestartAndExit(new[] { "--profile", _currentProfile.Id });
        }
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

    // Household step H2b: through UpdateBackupSettings, which records the
    // scheduled cloud backup policy in the household.
    private async Task SaveBackupSettingsAsync()
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

            var cloudEnabled = _cloudEnabledBox?.Checked ?? false;
            var cloudDirectory = _cloudDirectoryBox?.Text.Trim() ?? string.Empty;
            var cloudRetention = _cloudRetentionBox is null
                ? 30
                : (int)_cloudRetentionBox.Value;

            var cloudProvider = CloudTargetProvider;
            var cloudIsProvider = cloudProvider is not null;
            var cloudAccountId = CloudTargetAccountId;
            if (cloudEnabled && cloudIsProvider && string.IsNullOrEmpty(cloudAccountId))
            {
                MessageBox.Show(this,
                    _loc.Get(cloudProvider == CloudProvider.GoogleDrive
                        ? "Ui.SettingsDialog.CloudBackup.SignInFirst.GoogleDrive"
                        : "Ui.SettingsDialog.CloudBackup.SignInFirst"),
                    _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (cloudEnabled && !cloudIsProvider && string.IsNullOrWhiteSpace(cloudDirectory))
            {
                MessageBox.Show(this,
                    _loc.Get("Ui.CloudBackup.Error.FolderMissing"),
                    _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (cloudEnabled && !_cloudPassStore.HasPassphrase)
            {
                MessageBox.Show(this,
                    _loc.Get("Ui.CloudBackup.Error.PassphraseMissing"),
                    _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (cloudEnabled && !cloudIsProvider)
            {
                try { Directory.CreateDirectory(cloudDirectory); }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        _loc.Get("Ui.SettingsDialog.Backup.DirectoryCreateError", ex.Message),
                        _loc.Get("Ui.SettingsDialog.CloudBackup.Section.Title"),
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
                CloudFolderEnabled = cloudEnabled,
                CloudFolderDirectory = cloudDirectory,
                CloudFolderRetention = cloudRetention,
                // Without the provider choice the saved target is kept
                // (CloudTargetProvider, CloudTargetAccountId).
                CloudProvider = cloudProvider,
                CloudAccountId = cloudIsProvider ? cloudAccountId : string.Empty,
            };

            await RunUseCaseAsync<UpdateBackupSettings>(u => u.ExecuteAsync(settings, CancellationToken.None));
            if (IsDisposed) return;
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
        if (!await ConfirmSyncResetAsync(targetProfileId)) return;

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
            //
            // Pass "--profile <id>" so the restarted process opens
            // the same profile without going through the picker,
            // even if the registry contains more than one profile.
            if (isActive)
            {
                _restarter.RestartAndExit(new[] { "--profile", _currentProfile.Id });
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

    // C.3: launches the encrypted-export dialog. The dialog owns the
    // whole flow (destination, passphrase, opt-in scope, progress); the
    // SettingsDialog only opens it.
    private void ShowExportDialog()
    {
        using var dialog = new ExportDialog(_loc, _exportService, _currentProfile, _profileRegistry);
        dialog.ShowDialog(this);
    }

    // C.3: launches the encrypted-import dialog. On a successful import
    // the dialog reports whether the user accepted the restart prompt
    // (§4.2 step 11); the profile DB has been swapped from under EF
    // Core, so a restart into the same profile is the clean path.
    private async Task ShowImportDialog()
    {
        if (!await ConfirmSyncResetAsync(_currentProfile.Id)) return;
        using var dialog = new ImportDialog(_loc, _importService, _currentProfile, _profileRegistry);
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.OK && dialog.RestartRequested)
        {
            _restarter.RestartAndExit(new[] { "--profile", _currentProfile.Id });
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
            DialogLayout.GrowToContent(this);

            var prompt = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(420, 0),
                Text = loc.Get("Ui.SettingsDialog.Backup.RestoreInto.Prompt"),
            };

            _combo = new ComboBox
            {
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
                ForeColor = UiColors.Hint,
                Text = string.IsNullOrWhiteSpace(filenameProfileId)
                    ? loc.Get("Ui.SettingsDialog.Backup.RestoreInto.NoFilenameHint")
                    : loc.Get("Ui.SettingsDialog.Backup.RestoreInto.FilenameHint", filenameProfileId),
            };

            var okButton = DialogLayout.Button(loc.Get("Common.Ok"));
            var cancelButton = DialogLayout.Button(loc.Get("Common.Cancel"), DialogResult.Cancel);
            okButton.Click += (_, _) =>
            {
                if (_combo.SelectedItem is ProfileItem picked)
                {
                    SelectedProfileId = picked.Id;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            Controls.Add(DialogLayout.Stack(prompt, _combo, extractedNote));
            Controls.Add(DialogLayout.ButtonBar(this, okButton, cancelButton));
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
            _backupStatusLabel.ForeColor = UiColors.Hint;
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
                ? UiColors.Success
                : UiColors.Warning;
            _backupStatusLabel.Text =
                _loc.Get("Ui.SettingsDialog.Backup.LastOk", timestamp) + errorSuffix;
            return;
        }

        _backupStatusLabel.ForeColor = UiColors.Error;
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
}
