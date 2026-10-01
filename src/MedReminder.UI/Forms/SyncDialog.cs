using System.Globalization;
using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Sync;
using MedReminder.UI.Controls;
using MedReminder.UI.Hosting;
using MedReminder.UI.Services;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Forms;

// Tools → Sync… (B.1 Phase 3d, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §7.4, §4.5): status, devices, conflict review, enable, join, rebuild
// and disable for every profile, each on its own data (product owner,
// 2026-09-28; until then the administrator only, which left user
// profiles with no way to sync). Join and rebuild replace the profile
// database and restart the application, like an import.
// Phase 4a, 4b: the group lives in a OneDrive or Google Drive account
// (sign-in in
// the system browser) or in a folder; the choice is made when enabling or
// joining.
// Phase 4c: pairing codes (show one, join or take a new key with one),
// key rotation, device removal and the new key after a rotation on
// another device, for every profile as well: a sync group belongs to one
// profile, so these reach only that profile's devices and data (product
// owner, 2026-09-28).
internal sealed class SyncDialog : MedReminderFormBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SyncHostedService _sync;
    private readonly SyncStatus _status;
    private readonly ISyncSettingsStore _settings;
    private readonly ICloudAccountService _accounts;
    private readonly ICurrentProfile _profile;
    private readonly ILocalizationService _loc;
    private readonly IApplicationRestarter _restarter;

    private readonly Label _statusText;
    private readonly Button _enable;
    private readonly Button _join;
    private readonly Button _syncNow;
    private readonly Button _rebuild;
    private readonly Button _disable;
    private readonly Button _signIn;
    private readonly Button _joinCode;
    private readonly Button _pair;
    private readonly Button _rotate;
    private readonly Button _newKey;
    private readonly Button _removeDevice;
    private readonly ListView _devices;
    private readonly ListView _conflicts;
    private readonly Button _restore;
    private readonly Button _dismiss;
    private readonly Button _close;
    private CloudAccount? _account;
    // Actions in progress. While one runs, the window cannot be closed by
    // the user: the action would carry on against a disposed window, and
    // its outcome (a message, a restart) would be lost.
    private int _busy;

    public SyncDialog(
        IServiceScopeFactory scopes,
        SyncHostedService sync,
        SyncStatus status,
        ISyncSettingsStore settings,
        ICloudAccountService accounts,
        ICurrentProfile profile,
        ILocalizationService localization,
        IApplicationRestarter restarter)
    {
        _scopes = scopes;
        _sync = sync;
        _status = status;
        _settings = settings;
        _accounts = accounts;
        _profile = profile;
        _loc = localization;
        _restarter = restarter;

        Text = _loc.Get("Ui.SyncDialog.Title");
        Width = 1080;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        // Section list instead of tabs: tab headers stay light in dark mode.
        var sections = new SectionView();

        // Status
        var statusPage = new Panel { Padding = new Padding(12) };
        _statusText = new Label { Dock = DockStyle.Fill, AutoSize = false };
        _enable = Action("Ui.SyncDialog.Enable", async () => await EnableAsync());
        _join = Action("Ui.SyncDialog.Join", async () => await JoinAsync());
        _syncNow = Action("Ui.SyncDialog.SyncNow", async () => await SyncNowAsync());
        _rebuild = Action("Ui.SyncDialog.Rebuild", async () => await RebuildAsync());
        _disable = Action("Ui.SyncDialog.Disable", async () => await DisableAsync());
        _signIn = Action("Ui.SyncDialog.SignInAgain", async () => await SignInAgainAsync());
        _joinCode = Action("Ui.SyncDialog.JoinCode", async () => await JoinWithCodeAsync());
        _pair = Action("Ui.SyncDialog.Pair", async () => await PairAsync());
        _rotate = Action("Ui.SyncDialog.Rotate", async () => await RotateAsync(null));
        _newKey = Action("Ui.SyncDialog.NewKey", async () => await NewKeyAsync());
        var statusButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        statusButtons.Controls.AddRange([_syncNow, _signIn, _newKey, _enable, _join, _joinCode, _pair, _rotate, _rebuild, _disable]);
        statusPage.Controls.Add(_statusText);
        statusPage.Controls.Add(statusButtons);

        // Devices
        var devicesPage = new Panel { Padding = new Padding(12) };
        _devices = List(
            ("Ui.SyncDialog.Devices.Name", 220), ("Ui.SyncDialog.Devices.Platform", 110),
            ("Ui.SyncDialog.Devices.Version", 110), ("Ui.SyncDialog.Devices.LastSeen", 170));
        _devices.SelectedIndexChanged += (_, _) => UpdateDeviceButtons();
        _removeDevice = Action("Ui.SyncDialog.RemoveDevice", async () => await RemoveDeviceAsync());
        var deviceButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, UiTheme.Space.S, 0, 0) };
        deviceButtons.Controls.Add(_removeDevice);
        devicesPage.Controls.Add(_devices);
        devicesPage.Controls.Add(deviceButtons);

        // Conflicts
        var conflictsPage = new Panel { Padding = new Padding(12) };
        _conflicts = List(
            ("Ui.SyncDialog.Conflicts.Detected", 130), ("Ui.SyncDialog.Conflicts.Medicine", 150),
            ("Ui.SyncDialog.Conflicts.Kind", 200), ("Ui.SyncDialog.Conflicts.Field", 110),
            ("Ui.SyncDialog.Conflicts.Kept", 110), ("Ui.SyncDialog.Conflicts.Lost", 110));
        _conflicts.SelectedIndexChanged += (_, _) => UpdateConflictButtons();
        _restore = Action("Ui.SyncDialog.Conflicts.Restore", async () => await RestoreAsync());
        _dismiss = Action("Ui.SyncDialog.Conflicts.Dismiss", async () => await DismissAsync());
        var conflictButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, UiTheme.Space.S, 0, 0) };
        conflictButtons.Controls.AddRange([_restore, _dismiss]);
        conflictsPage.Controls.Add(_conflicts);
        conflictsPage.Controls.Add(conflictButtons);

        sections.AddSection(_loc.Get("Ui.SyncDialog.Tab.Status"), Mdl2Glyph.Glyphs.Info, statusPage);
        sections.AddSection(_loc.Get("Ui.SyncDialog.Tab.Devices"), Mdl2Glyph.Glyphs.Devices, devicesPage);
        sections.AddSection(_loc.Get("Ui.SyncDialog.Tab.Conflicts"), Mdl2Glyph.Glyphs.Warning, conflictsPage);

        var close = _close = DialogLayout.Button(_loc.Get("Common.Close"), DialogResult.OK);
        var bottom = DialogLayout.ButtonBar(this, close, close);
        Controls.Add(sections);
        Controls.Add(bottom);

        _status.Changed += OnStatusChanged;
        FormClosed += (_, _) => _status.Changed -= OnStatusChanged;
        FormClosing += (_, e) =>
        {
            if (_busy > 0 && e.CloseReason is CloseReason.UserClosing or CloseReason.None) e.Cancel = true;
        };
        Shown += async (_, _) =>
        {
            try
            {
                await RefreshAllAsync();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                // Closed while the lists were loading.
            }
        };
    }

    private bool IsEnabled => _settings.Load() is not null;

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Action(RefreshStatus));
    }

    private async Task RefreshAllAsync()
    {
        await RefreshAccountAsync();
        RefreshStatus();
        await RefreshDevicesAsync();
        await RefreshConflictsAsync();
    }

    private void RefreshStatus()
    {
        var settings = _settings.Load();
        _enable.Visible = settings is null;
        _join.Visible = settings is null;
        _disable.Visible = settings is not null;
        _joinCode.Visible = settings is null;
        _pair.Visible = settings is not null && !_status.NeedsNewKey;
        _rotate.Visible = settings is not null && !_status.NeedsNewKey;
        _newKey.Visible = settings is not null && _status.NeedsNewKey;
        _removeDevice.Visible = settings is not null;
        UpdateDeviceButtons();
        _syncNow.Visible = settings is not null;
        _rebuild.Visible = settings is not null && _status.NeedsRebuild;
        _signIn.Visible = settings?.Provider is not null && (_status.NeedsSignIn || _account is null);
        if (settings?.Provider is { } signInProvider) _signIn.Text = _loc.Get(ProviderKey("Ui.SyncDialog.SignInAgain", signInProvider));

        if (settings is null)
        {
            _statusText.Text = _loc.Get("Ui.SyncDialog.Status.Disabled");
            return;
        }

        var lines = new List<string>
        {
            settings.Provider switch
            {
                null => _loc.Get("Ui.SyncDialog.Status.Folder", settings.Folder ?? string.Empty),
                CloudProvider.GoogleDrive when _account is not null => _loc.Get("Ui.SyncDialog.Status.GoogleDrive", _account.UserName),
                CloudProvider.GoogleDrive => _loc.Get("Ui.SyncDialog.Status.GoogleDriveSignedOut"),
                _ when _account is not null => _loc.Get("Ui.SyncDialog.Status.OneDrive", _account.UserName),
                _ => _loc.Get("Ui.SyncDialog.Status.OneDriveSignedOut"),
            },
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
        if (_status.NeedsNewKey) lines.Add(_loc.Get("Ui.SyncDialog.Status.NeedsNewKey"));
        if (_status.NeedsSignIn)
        {
            lines.Add(_loc.Get(ProviderKey("Ui.SyncDialog.Status.NeedsSignIn", settings.Provider ?? CloudProvider.OneDrive)));
        }
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
                var row = new ListViewItem(name) { Tag = record };
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

    private DeviceRecordContent? SelectedDevice
        => _devices.SelectedItems.Count == 1 ? _devices.SelectedItems[0].Tag as DeviceRecordContent : null;

    // Another device only: this one is removed by disabling sync.
    private void UpdateDeviceButtons()
        => _removeDevice.Enabled = SelectedDevice is { } device && device.DeviceId != _settings.Load()?.DeviceId;

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
        if (IsEnabled) return;
        var target = await ChooseTargetAsync();
        if (target is null) return;
        using var dialog = new SyncPassphraseDialog(_loc, confirm: true, Environment.MachineName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var passphrase = dialog.TakePassphrase();
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                .CreateAsync(target, passphrase, dialog.DeviceName, CancellationToken.None);
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
        if (IsEnabled) return;
        var target = await ChooseTargetAsync();
        if (target is null) return;

        IReadOnlyList<Guid> groups;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            groups = await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                .ListGroupsAsync(target, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }
        if (groups.Count == 0)
        {
            Error(_loc.Get(target.Provider is { } cloud
                ? ProviderKey("Ui.SyncDialog.Join.NoGroupCloud", cloud)
                : "Ui.SyncDialog.Join.NoGroup"));
            return;
        }
        // One group per profile: a folder shared by several profiles has
        // several. The passphrase tells them apart; when it opens more than
        // one, the user chooses by the devices of each group.
        using var dialog = new SyncPassphraseDialog(_loc, confirm: false, Environment.MachineName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var passphrase = dialog.TakePassphrase();
        try
        {
            IReadOnlyList<SyncGroupCandidate> opened;
            await using (var scope = _scopes.CreateAsyncScope())
            {
                opened = await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                    .FindGroupsAsync(target, passphrase, CancellationToken.None);
            }
            if (opened.Count == 0)
            {
                Error(_loc.Get("Ui.SyncDialog.Join.WrongPassphrase"));
                return;
            }
            var group = opened[0].GroupId;
            if (opened.Count > 1)
            {
                using var choice = new SyncGroupChoiceDialog(_loc, opened);
                if (choice.ShowDialog(this) != DialogResult.OK || choice.SelectedGroupId is not { } chosen) return;
                group = chosen;
            }
            if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.SyncDialog.Join.Confirm", _profile.DisplayName),
                    _loc.Get("Ui.SyncDialog.Join.ConfirmTitle"), MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                    .JoinAsync(target, group, passphrase, dialog.DeviceName, CancellationToken.None);
            }
            catch (CryptographicException)
            {
                // The group was re-keyed between the search and the join.
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
        if (!IsEnabled) return;
        if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.SyncDialog.Rebuild.Confirm"), _loc.Get("Ui.SyncDialog.Rebuild"),
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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
        if (!IsEnabled) return;
        if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.SyncDialog.Disable.Confirm"), _loc.Get("Ui.SyncDialog.Disable"),
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
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

    // Phase 4c: joins with a pairing code shown by a paired device. The
    // code names the storage kind; a cloud account is signed in to here.
    private async Task JoinWithCodeAsync()
    {
        if (IsEnabled) return;
        using var dialog = new SyncPairingCodeDialog(_loc, Environment.MachineName);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Code is not { } code) return;

        var target = await Picker.ForCodeAsync(code.Provider);
        if (target is null) return;
        if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.SyncDialog.Join.Confirm", _profile.DisplayName),
                _loc.Get("Ui.SyncDialog.Join.ConfirmTitle"), MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                .JoinWithPairingAsync(target, code, dialog.DeviceName, CancellationToken.None);
            Info(_loc.Get("Ui.SyncDialog.Restart"));
            _restarter.RestartAndExit(["--profile", _profile.Id]);
        }
        catch (Exception ex)
        {
            Error(PairingError(ex));
        }
        finally
        {
            Array.Clear(code.Secret);
        }
    }

    // Phase 4c: shows a pairing code for another device. The offer ends
    // (its file is deleted) when the dialog closes.
    private async Task PairAsync()
    {
        if (!IsEnabled) return;
        await using var scope = _scopes.CreateAsyncScope();
        var transport = scope.ServiceProvider.GetRequiredService<ISyncTransport>();
        var offers = scope.ServiceProvider.GetRequiredService<SyncPairingOffers>();
        SyncPairingOffer offer;
        try
        {
            offer = await offers.StartAsync(transport, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }

        try
        {
            using var dialog = new SyncPairingDialog(_loc, offer, TimeProvider.System);
            dialog.ShowDialog(this);
        }
        finally
        {
            Array.Clear(offer.Code.Secret);
            try
            {
                await offers.EndAsync(transport, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Error(_loc.Get("Ui.SyncDialog.Pair.EndFailed", ex.Message));
            }
        }
    }

    private async Task RemoveDeviceAsync()
    {
        if (SelectedDevice is { } device && device.DeviceId != _settings.Load()?.DeviceId)
            await RotateAsync(device.Name);
    }

    // Phase 4c (§6.2): a new group key and passphrase. With a device name,
    // that device is being removed: it is not given the new key.
    private async Task RotateAsync(string? removedDevice)
    {
        if (!IsEnabled) return;
        var confirm = removedDevice is null
            ? _loc.Get("Ui.SyncDialog.Rotate.Confirm")
            : _loc.Get("Ui.SyncDialog.RemoveDevice.Confirm", removedDevice);
        if (ConfirmDialog.Show(_loc, this, confirm, _loc.Get(removedDevice is null ? "Ui.SyncDialog.Rotate" : "Ui.SyncDialog.RemoveDevice"),
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        using var dialog = new SyncPassphraseDialog(_loc, confirm: true, defaultDeviceName: null,
            "Ui.SyncDialog.Rotate.Title", "Ui.SyncDialog.Rotate.Hint");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var passphrase = dialog.TakePassphrase();
        try
        {
            // The genesis of the new generation must hold the latest
            // changes of every device.
            await _sync.RunNowAsync();
            await _sync.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<RotateSyncKey>().ExecuteAsync(
                    scope.ServiceProvider.GetRequiredService<ISyncTransport>(), passphrase, CancellationToken.None);
            });
            Info(_loc.Get("Ui.SyncDialog.Rotate.Done"));
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

    // Phase 4c: the key was changed on another device. The new key comes
    // from the new passphrase or a pairing code; the profile is rebuilt
    // from the new generation and this device's changes are kept.
    private async Task NewKeyAsync()
    {
        if (!IsEnabled) return;
        const int passphraseChoice = 0, codeChoice = 1;
        var choice = ChoiceDialog.Show(this, _loc, Text, _loc.Get("Ui.SyncDialog.NewKey.Heading"),
            _loc.Get("Ui.SyncDialog.NewKey.Text"),
        [
            (_loc.Get("Ui.SyncDialog.NewKey.Passphrase"), _loc.Get("Ui.SyncDialog.NewKey.PassphraseNote")),
            (_loc.Get("Ui.SyncDialog.NewKey.Code"), _loc.Get("Ui.SyncDialog.NewKey.CodeNote")),
        ]);

        SyncKeySource source;
        if (choice == passphraseChoice)
        {
            using var dialog = new SyncPassphraseDialog(_loc, confirm: false, defaultDeviceName: null,
                "Ui.SyncDialog.NewKey.Title", "Ui.SyncDialog.NewKey.PassphraseHint");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            source = new SyncKeySource.Passphrase(dialog.TakePassphrase());
        }
        else if (choice == codeChoice)
        {
            using var dialog = new SyncPairingCodeDialog(_loc, defaultDeviceName: null);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Code is not { } code) return;
            if (code.GroupId != _settings.Load()?.GroupId)
            {
                Error(_loc.Get("Ui.SyncDialog.PairingCode.OtherGroup"));
                return;
            }
            source = new SyncKeySource.Pairing(code);
        }
        else
        {
            return;
        }

        try
        {
            var (carried, dropped) = await _sync.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ISyncSetupService>()
                    .RekeyAsync(source, CancellationToken.None);
            });
            Info(_loc.Get(dropped == 0 ? "Ui.SyncDialog.NewKey.Done" : "Ui.SyncDialog.NewKey.DoneDropped", carried, dropped));
            _restarter.RestartAndExit(["--profile", _profile.Id]);
        }
        catch (CryptographicException) when (source is SyncKeySource.Passphrase)
        {
            Error(_loc.Get("Ui.SyncDialog.NewKey.WrongPassphrase"));
        }
        catch (Exception ex)
        {
            Error(PairingError(ex));
        }
        finally
        {
            if (source is SyncKeySource.Passphrase p) Array.Clear(p.Value);
            if (source is SyncKeySource.Pairing c) Array.Clear(c.Code.Secret);
        }
    }

    private string PairingError(Exception ex) => ex switch
    {
        SyncPairingExpiredException => _loc.Get("Ui.SyncDialog.PairingCode.Expired"),
        CryptographicException => _loc.Get("Ui.SyncDialog.PairingCode.Rejected"),
        _ => ex.Message,
    };

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

    private async Task RefreshAccountAsync()
    {
        _account = null;
        if (_settings.Load() is not { Provider: { } provider, AccountId: { } accountId }) return;
        try
        {
            _account = await _accounts.FindAsync(provider, accountId, CancellationToken.None);
        }
        catch (Exception)
        {
            // Shown as signed out; "Sign in again" repairs it.
        }
    }

    private StorageTargetPicker Picker => new(this, _accounts, _loc, Text);

    private Task<SyncTarget?> ChooseTargetAsync() => Picker.ChooseAsync();

    private Task<CloudAccount?> SignInAsync(CloudProvider provider, string? accountId)
        => Picker.SignInAsync(provider, accountId);

    private async Task SignInAgainAsync()
    {
        if (_settings.Load() is not { Provider: { } provider, AccountId: { } accountId }) return;
        var account = await SignInAsync(provider, accountId);
        if (account is null) return;
        if (account.Id != accountId)
        {
            // Another account holds another folder, not this group.
            Error(_loc.Get(ProviderKey("Ui.SyncDialog.SignIn.OtherAccount", provider)));
            return;
        }
        await _sync.RunNowAsync();
        await RefreshAllAsync();
    }

    private static string ProviderKey(string key, CloudProvider provider)
        => StorageTargetPicker.ProviderKey(key, provider);

    private Button Action(string key, Func<Task> action)
    {
        var button = DialogLayout.Button(_loc.Get(key));
        button.Margin = new Padding(0, 0, UiTheme.Space.S, 0);
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            _busy++;
            UpdateBusy();
            try
            {
                await action();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                // Closed by the application (exit, restart) while the
                // action ran: there is nothing left to update.
            }
            finally
            {
                _busy--;
                if (!IsDisposed)
                {
                    button.Enabled = true;
                    UpdateBusy();
                    UpdateConflictButtons();
                    UpdateDeviceButtons();
                }
            }
        };
        return button;
    }

    private void UpdateBusy()
    {
        _close.Enabled = _busy == 0;
        UseWaitCursor = _busy > 0;
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

    // No owner once the window is gone (application exit while an action
    // ran): the message is dropped rather than thrown from the action.
    private void Info(string message)
    {
        if (!IsDisposed) UiMessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Error(string message)
    {
        if (!IsDisposed) UiMessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
