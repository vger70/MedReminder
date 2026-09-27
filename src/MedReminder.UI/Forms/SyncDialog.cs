using System.Globalization;
using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Sync;
using MedReminder.UI.Hosting;
using MedReminder.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Forms;

// Tools → Sync… (B.1 Phase 3d, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §7.4, §4.5): status, devices and conflict review for everyone; enable,
// join, rebuild and disable for the administrator only (product owner,
// 2026-09-27). Join and rebuild replace the profile database and restart
// the application, like an import.
internal sealed class SyncDialog : MedReminderFormBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SyncHostedService _sync;
    private readonly SyncStatus _status;
    private readonly ISyncSettingsStore _settings;
    private readonly ICurrentProfile _profile;
    private readonly ILocalizationService _loc;
    private readonly IApplicationRestarter _restarter;

    private readonly Label _statusText;
    private readonly Button _enable;
    private readonly Button _join;
    private readonly Button _syncNow;
    private readonly Button _rebuild;
    private readonly Button _disable;
    private readonly ListView _devices;
    private readonly ListView _conflicts;
    private readonly Button _restore;
    private readonly Button _dismiss;

    public SyncDialog(
        IServiceScopeFactory scopes,
        SyncHostedService sync,
        SyncStatus status,
        ISyncSettingsStore settings,
        ICurrentProfile profile,
        ILocalizationService localization,
        IApplicationRestarter restarter)
    {
        _scopes = scopes;
        _sync = sync;
        _status = status;
        _settings = settings;
        _profile = profile;
        _loc = localization;
        _restarter = restarter;

        Text = _loc.Get("Ui.SyncDialog.Title");
        Width = 880;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.75F);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // Status
        var statusPage = new TabPage(_loc.Get("Ui.SyncDialog.Tab.Status")) { Padding = new Padding(12) };
        _statusText = new Label { Dock = DockStyle.Fill, AutoSize = false };
        _enable = Action("Ui.SyncDialog.Enable", async () => await EnableAsync());
        _join = Action("Ui.SyncDialog.Join", async () => await JoinAsync());
        _syncNow = Action("Ui.SyncDialog.SyncNow", async () => await SyncNowAsync());
        _rebuild = Action("Ui.SyncDialog.Rebuild", async () => await RebuildAsync());
        _disable = Action("Ui.SyncDialog.Disable", async () => await DisableAsync());
        var statusButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        statusButtons.Controls.AddRange([_syncNow, _enable, _join, _rebuild, _disable]);
        statusPage.Controls.Add(_statusText);
        statusPage.Controls.Add(statusButtons);

        // Devices
        var devicesPage = new TabPage(_loc.Get("Ui.SyncDialog.Tab.Devices")) { Padding = new Padding(12) };
        _devices = List(
            ("Ui.SyncDialog.Devices.Name", 220), ("Ui.SyncDialog.Devices.Platform", 110),
            ("Ui.SyncDialog.Devices.Version", 110), ("Ui.SyncDialog.Devices.LastSeen", 170));
        devicesPage.Controls.Add(_devices);

        // Conflicts
        var conflictsPage = new TabPage(_loc.Get("Ui.SyncDialog.Tab.Conflicts")) { Padding = new Padding(12) };
        _conflicts = List(
            ("Ui.SyncDialog.Conflicts.Detected", 130), ("Ui.SyncDialog.Conflicts.Medicine", 150),
            ("Ui.SyncDialog.Conflicts.Kind", 200), ("Ui.SyncDialog.Conflicts.Field", 110),
            ("Ui.SyncDialog.Conflicts.Kept", 110), ("Ui.SyncDialog.Conflicts.Lost", 110));
        _conflicts.SelectedIndexChanged += (_, _) => UpdateConflictButtons();
        _restore = Action("Ui.SyncDialog.Conflicts.Restore", async () => await RestoreAsync());
        _dismiss = Action("Ui.SyncDialog.Conflicts.Dismiss", async () => await DismissAsync());
        var conflictButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        conflictButtons.Controls.AddRange([_restore, _dismiss]);
        conflictsPage.Controls.Add(_conflicts);
        conflictsPage.Controls.Add(conflictButtons);

        tabs.TabPages.AddRange([statusPage, devicesPage, conflictsPage]);

        var close = new Button { Text = _loc.Get("Common.Close"), DialogResult = DialogResult.OK, AutoSize = true, Height = 32 };
        var bottom = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        bottom.Controls.Add(close);
        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = close;

        _status.Changed += OnStatusChanged;
        FormClosed += (_, _) => _status.Changed -= OnStatusChanged;
        Shown += async (_, _) => await RefreshAllAsync();
    }

    private bool IsEnabled => _settings.Load() is not null;

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(RefreshStatus));
    }

    private async Task RefreshAllAsync()
    {
        RefreshStatus();
        await RefreshDevicesAsync();
        await RefreshConflictsAsync();
    }

    private void RefreshStatus()
    {
        var settings = _settings.Load();
        var admin = _profile.IsAdmin;
        _enable.Visible = admin && settings is null;
        _join.Visible = admin && settings is null;
        _disable.Visible = admin && settings is not null;
        _syncNow.Visible = settings is not null;
        _rebuild.Visible = admin && settings is not null && _status.NeedsRebuild;

        if (settings is null)
        {
            _statusText.Text = _loc.Get("Ui.SyncDialog.Status.Disabled");
            return;
        }

        var lines = new List<string>
        {
            _loc.Get("Ui.SyncDialog.Status.Folder", settings.Folder ?? string.Empty),
            _loc.Get("Ui.SyncDialog.Status.Device", settings.DeviceName ?? Environment.MachineName),
            _loc.Get("Ui.SyncDialog.Status.Generation", settings.Generation),
            _status.LastRunAt is { } at
                ? _loc.Get("Ui.SyncDialog.Status.LastRun", at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : _loc.Get("Ui.SyncDialog.Status.NeverRun"),
        };
        if (_status.LastResult is { } result)
        {
            lines.Add(_loc.Get("Ui.SyncDialog.Status.Result",
                result.OperationsPublished, result.OperationsApplied));
            lines.AddRange(result.Problems);
        }
        if (_status.LastError is { } error) lines.Add(_loc.Get("Ui.SyncDialog.Status.Error", error));
        if (settings.ResetPending) lines.Add(_loc.Get("Ui.SyncDialog.Status.ResetPending"));
        if (_status.NeedsRebuild) lines.Add(_loc.Get("Ui.SyncDialog.Status.NeedsRebuild"));
        _statusText.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task RefreshDevicesAsync()
    {
        _devices.Items.Clear();
        if (!IsEnabled) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<SyncEngine>().ListDevicesAsync(CancellationToken.None);
            var me = _settings.Load()?.DeviceId;
            foreach (var record in records)
            {
                var name = record.DeviceId == me ? _loc.Get("Ui.SyncDialog.Devices.ThisDevice", record.Name) : record.Name;
                var row = new ListViewItem(name);
                row.SubItems.Add(record.Platform);
                row.SubItems.Add(record.AppVersion);
                row.SubItems.Add(record.LastSeen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                _devices.Items.Add(row);
            }
        }
        catch (Exception ex)
        {
            _devices.Items.Add(new ListViewItem(_loc.Get("Ui.SyncDialog.Devices.Unavailable", ex.Message)));
        }
    }

    private async Task RefreshConflictsAsync()
    {
        _conflicts.Items.Clear();
        await using var scope = _scopes.CreateAsyncScope();
        foreach (var item in await scope.ServiceProvider.GetRequiredService<SyncConflictsQuery>().LoadAsync(CancellationToken.None))
        {
            var c = item.Conflict;
            var row = new ListViewItem(c.DetectedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) { Tag = item };
            row.SubItems.Add(item.MedicineName);
            row.SubItems.Add(_loc.Get($"Ui.SyncDialog.Conflicts.Kind.{c.Kind}"));
            row.SubItems.Add(c.Register ?? string.Empty);
            row.SubItems.Add(c.WinningValue ?? string.Empty);
            row.SubItems.Add(c.LosingValue ?? string.Empty);
            _conflicts.Items.Add(row);
        }
        UpdateConflictButtons();
    }

    private SyncConflictItem? SelectedConflict
        => _conflicts.SelectedItems.Count == 1 ? _conflicts.SelectedItems[0].Tag as SyncConflictItem : null;

    private void UpdateConflictButtons()
    {
        _restore.Enabled = SelectedConflict is { CanRestore: true };
        _dismiss.Enabled = SelectedConflict is not null;
    }

    private async Task SyncNowAsync()
    {
        await _sync.RunNowAsync();
        await RefreshAllAsync();
    }

    private async Task EnableAsync()
    {
        if (!_profile.IsAdmin || IsEnabled) return;
        var folder = PickFolder();
        if (folder is null) return;
        using var dialog = new SyncPassphraseDialog(_loc, confirm: true, Environment.MachineName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var passphrase = dialog.TakePassphrase();
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                .CreateAsync(folder, passphrase, dialog.DeviceName, CancellationToken.None);
            Info(_loc.Get("Ui.SyncDialog.Enable.Done"));
            await _sync.RunNowAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            Array.Clear(passphrase);
        }
        await RefreshAllAsync();
    }

    private async Task JoinAsync()
    {
        if (!_profile.IsAdmin || IsEnabled) return;
        var folder = PickFolder();
        if (folder is null) return;

        IReadOnlyList<Guid> groups;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            groups = await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                .ListGroupsAsync(folder, CancellationToken.None);
        }
        if (groups.Count == 0)
        {
            Error(_loc.Get("Ui.SyncDialog.Join.NoGroup"));
            return;
        }
        // One group per profile: a folder shared by several profiles has
        // several. The passphrase tells them apart; the first that opens
        // is joined.
        using var dialog = new SyncPassphraseDialog(_loc, confirm: false, Environment.MachineName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, _loc.Get("Ui.SyncDialog.Join.Confirm", _profile.DisplayName),
                _loc.Get("Ui.SyncDialog.Join.ConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var passphrase = dialog.TakePassphrase();
        try
        {
            Exception? last = null;
            foreach (var group in groups)
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                        .JoinAsync(folder, group, passphrase, dialog.DeviceName, CancellationToken.None);
                    last = null;
                    break;
                }
                catch (CryptographicException ex)
                {
                    last = ex;
                }
            }
            if (last is not null)
            {
                Error(_loc.Get("Ui.SyncDialog.Join.WrongPassphrase"));
                return;
            }
            Info(_loc.Get("Ui.SyncDialog.Restart"));
            _restarter.RestartAndExit(["--profile", _profile.Id]);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            Array.Clear(passphrase);
        }
    }

    private async Task RebuildAsync()
    {
        if (!_profile.IsAdmin || !IsEnabled) return;
        if (MessageBox.Show(this, _loc.Get("Ui.SyncDialog.Rebuild.Confirm"), _loc.Get("Ui.SyncDialog.Rebuild"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await _sync.SuspendAsync();
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISyncSetupService>().RebuildAsync(CancellationToken.None);
            Info(_loc.Get("Ui.SyncDialog.Restart"));
            _restarter.RestartAndExit(["--profile", _profile.Id]);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private async Task DisableAsync()
    {
        if (!_profile.IsAdmin || !IsEnabled) return;
        if (MessageBox.Show(this, _loc.Get("Ui.SyncDialog.Disable.Confirm"), _loc.Get("Ui.SyncDialog.Disable"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await _sync.RunNowAsync();
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DisableSync>().ExecuteAsync(CancellationToken.None);
            _status.Reset();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        await RefreshAllAsync();
    }

    private async Task RestoreAsync()
    {
        if (SelectedConflict is not { CanRestore: true } item) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RestoreSyncConflict>()
                .ExecuteAsync(item.Conflict.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        await RefreshConflictsAsync();
    }

    private async Task DismissAsync()
    {
        if (SelectedConflict is not { } item) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DismissSyncConflict>()
                .ExecuteAsync(item.Conflict.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        await RefreshConflictsAsync();
    }

    private string? PickFolder()
    {
        using var browser = new FolderBrowserDialog
        {
            Description = _loc.Get("Ui.SyncDialog.Folder"),
            UseDescriptionForTitle = true,
        };
        return browser.ShowDialog(this) == DialogResult.OK ? browser.SelectedPath : null;
    }

    private Button Action(string key, Func<Task> action)
    {
        var button = new Button { Text = _loc.Get(key), AutoSize = true, Height = 32 };
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try
            {
                await action();
            }
            finally
            {
                button.Enabled = true;
                UpdateConflictButtons();
            }
        };
        return button;
    }

    private ListView List(params (string Key, int Width)[] columns)
    {
        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            Dock = DockStyle.Fill,
        };
        foreach (var (key, width) in columns) list.Columns.Add(_loc.Get(key), width);
        return list;
    }

    private void Info(string message)
        => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void Error(string message)
        => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
