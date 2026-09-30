using System.Globalization;
using System.Security.Cryptography;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Sync.Remote;
using MedReminder.Domain.Household;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Forms;

// Tools → Installation… (household step H3d; docs/analysis/
// ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.3, §6): administrators only.
//
//   - Publish the installation on a storage, with the household
//     passphrase (an administrator's secret, D-13).
//   - Devices of the installation; Add a device shows an mrpair2 code for
//     the profiles selected, for 10 minutes or until the window closes.
//   - Join an existing installation, with a code shown by one of its
//     devices or with the household passphrase and the approval of one of
//     its administrators; the granted profiles are brought to this device
//     and the application restarts. The profiles already on this device
//     are added to the installation at the next start (ReconcileHousehold),
//     and the installation's settings replace this device's.
//
// Step H3d-2: with setup set, the window is the join of the first-run
// wizard (§6.1). No profile exists yet and no database is open: the join
// starts at once, needs no confirmation (nothing on this device is
// replaced), and a join that brings profiles closes the window with OK
// instead of restarting; the start goes on with those profiles.
internal sealed class HouseholdDialog : MedReminderFormBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HouseholdHostedService _household;
    private readonly ICloudAccountService _accounts;
    private readonly ICurrentProfile _profile;
    private readonly ILocalizationService _loc;
    private readonly IApplicationRestarter _restarter;

    private readonly Label _statusText;
    private readonly Button _publish;
    private readonly Button _join;
    private readonly Button _syncNow;
    private readonly Button _handover;
    private readonly Button _newKey;
    private readonly Button _removeDevice;
    private bool _needsNewKey;
    private readonly Button _addDevice;
    private readonly Button _makeMaster;
    // Step H4a: device names from the last device list, for the master line.
    private Dictionary<Guid, string> _deviceNames = [];
    private Dictionary<Guid, DateTimeOffset> _lastSeen = [];
    private MasterView? _master;
    private readonly ListView _devices;
    private readonly Button _close;
    private readonly bool _setup;
    // The first-run join is done: the window closes although the action
    // that did it is still on the stack.
    private bool _completed;
    private HouseholdIdentity? _identity;
    private int _busy;

    public HouseholdDialog(IServiceScopeFactory scopes, HouseholdHostedService household, ICloudAccountService accounts,
        ICurrentProfile profile, ILocalizationService localization, IApplicationRestarter restarter, bool setup = false)
    {
        _setup = setup;
        _scopes = scopes;
        _household = household;
        _accounts = accounts;
        _profile = profile;
        _loc = localization;
        _restarter = restarter;

        Text = _loc.Get("Ui.HouseholdDialog.Title");
        Width = 820;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var statusPage = new TabPage(_loc.Get("Ui.HouseholdDialog.Tab.Status")) { Padding = new Padding(12) };
        _statusText = new Label { Dock = DockStyle.Fill, AutoSize = false };
        _publish = Action("Ui.HouseholdDialog.Publish", PublishAsync);
        _join = Action("Ui.HouseholdDialog.Join", JoinAsync);
        _syncNow = Action("Ui.SyncDialog.SyncNow", SyncNowAsync);
        _handover = Action("Ui.HouseholdDialog.Handover", HandoverAsync);
        _handover.Visible = false;
        _newKey = Action("Ui.HouseholdDialog.NewKey", NewKeyAsync);
        _newKey.Visible = false;
        var statusButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        statusButtons.Controls.AddRange([_syncNow, _newKey, _handover, _publish, _join]);
        statusPage.Controls.Add(_statusText);
        statusPage.Controls.Add(statusButtons);

        var devicesPage = new TabPage(_loc.Get("Ui.HouseholdDialog.Tab.Devices")) { Padding = new Padding(12) };
        _devices = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill };
        foreach (var (key, width) in new[]
                 {
                     ("Ui.SyncDialog.Devices.Name", 240), ("Ui.SyncDialog.Devices.Platform", 110),
                     ("Ui.SyncDialog.Devices.Version", 110), ("Ui.SyncDialog.Devices.LastSeen", 170),
                     ("Ui.HouseholdDialog.Devices.Role", 140),
                 })
        {
            _devices.Columns.Add(_loc.Get(key), width);
        }
        _addDevice = Action("Ui.HouseholdDialog.AddDevice", AddDeviceAsync);
        var deviceButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        deviceButtons.Controls.Add(_addDevice);
        _makeMaster = Action("Ui.HouseholdDialog.MakeMaster", MakeMasterAsync);
        deviceButtons.Controls.Add(_makeMaster);
        _removeDevice = Action("Ui.HouseholdDialog.RemoveDevice", RemoveDeviceAsync);
        deviceButtons.Controls.Add(_removeDevice);
        _devices.SelectedIndexChanged += (_, _) => UpdateDeviceButtons();
        devicesPage.Controls.Add(_devices);
        devicesPage.Controls.Add(deviceButtons);

        tabs.TabPages.AddRange([statusPage, devicesPage]);

        // During the first-run join, closing means no profile was brought.
        _close = new Button
        {
            Text = _loc.Get("Common.Close"),
            DialogResult = setup ? DialogResult.Cancel : DialogResult.OK,
            AutoSize = true,
            Height = 32,
        };
        var bottom = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        bottom.Controls.Add(_close);
        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = _close;

        _household.Changed += OnHouseholdChanged;
        FormClosed += (_, _) => _household.Changed -= OnHouseholdChanged;
        FormClosing += (_, e) =>
        {
            if (_busy > 0 && !_completed && e.CloseReason is CloseReason.UserClosing or CloseReason.None) e.Cancel = true;
        };
        Shown += async (_, _) =>
        {
            try
            {
                await RefreshAllAsync();
                if (_setup) _join.PerformClick();
            }
            catch (ObjectDisposedException) when (IsDisposed)
            {
                // Closed while loading.
            }
        };
    }

    private bool IsPublished => _identity?.Storage is not null;

    private void OnHouseholdChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(new Func<Task>(RefreshStatusAsync));
    }

    private async Task RefreshAllAsync()
    {
        await RefreshStatusAsync();
        await RefreshDevicesAsync();
        await RefreshStatusAsync();
    }

    private Guid? SelectedDevice => _devices.SelectedItems.Count == 1 ? _devices.SelectedItems[0].Tag as Guid? : null;

    private void UpdateDeviceButtons()
    {
        _makeMaster.Enabled = IsPublished && !_needsNewKey && SelectedDevice is { } device
            && _master?.Master.Election?.DeviceId != device;
        _removeDevice.Enabled = IsPublished && !_needsNewKey && SelectedDevice is { } other && other != _identity?.DeviceId;
    }

    private async Task RefreshStatusAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        _identity = await scope.ServiceProvider.GetRequiredService<IHouseholdStore>().EnsureCreatedAsync(CancellationToken.None);
        // No profile database is open during the first-run join.
        var link = _setup
            ? new HouseholdLinkStatus(HouseholdLinkState.None, null)
            : await scope.ServiceProvider.GetRequiredService<HouseholdLinks>().StatusAsync(CancellationToken.None);
        _master = await scope.ServiceProvider.GetRequiredService<HouseholdMasterRole>().DescribeAsync(CancellationToken.None);
        var handover = _setup ? null
            : await scope.ServiceProvider.GetRequiredService<MasterHandover>().PendingAsync(CancellationToken.None);
        try
        {
            _needsNewKey = !_setup
                && await scope.ServiceProvider.GetRequiredService<HouseholdSync>().NeedsNewKeyAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Storage offline: the last run's outcome says it.
            _needsNewKey = _household.LastResult?.NewKeyRequired == true;
        }
        if (IsDisposed) return;

        _publish.Visible = !IsPublished && !_setup;
        _join.Visible = !IsPublished;
        _syncNow.Visible = IsPublished;
        _handover.Visible = handover is not null && !_needsNewKey;
        _newKey.Visible = _needsNewKey;
        _addDevice.Enabled = IsPublished;

        var lines = new List<string>();
        if (_identity.Storage is not { } storage)
        {
            lines.Add(_loc.Get(_setup ? "Ui.HouseholdDialog.Status.Setup" : "Ui.HouseholdDialog.Status.NotPublished"));
        }
        else
        {
            lines.Add(storage.Provider switch
            {
                null => _loc.Get("Ui.HouseholdDialog.Status.Folder", storage.Folder ?? string.Empty),
                CloudProvider.GoogleDrive => _loc.Get("Ui.HouseholdDialog.Status.GoogleDrive"),
                _ => _loc.Get("Ui.HouseholdDialog.Status.OneDrive"),
            });
            lines.Add(_loc.Get("Ui.SyncDialog.Status.Device", _identity.DeviceName ?? Environment.MachineName));
            lines.Add(_household.LastRunAt is { } at
                ? _loc.Get("Ui.SyncDialog.Status.LastRun", at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : _loc.Get("Ui.SyncDialog.Status.NeverRun"));
            if (_household.LastResult is { } result)
            {
                lines.Add(_loc.Get("Ui.SyncDialog.Status.Result", result.OperationsPublished, result.OperationsApplied));
                lines.AddRange(result.Problems);
            }
            if (_household.LastError is { } error) lines.Add(_loc.Get("Ui.SyncDialog.Status.Error", error));
            // Step H5a: a device was removed elsewhere.
            if (_needsNewKey) lines.Add(_loc.Get("Ui.HouseholdDialog.Status.NewKey"));
            lines.Add(string.Empty);
            lines.Add(MasterLine(_master));
        }
        // §11: this profile's group was claimed first by another installation.
        if (link.State == HouseholdLinkState.OtherHousehold) lines.Add(_loc.Get("Ui.HouseholdDialog.Status.LinkedElsewhere"));
        _statusText.Text = string.Join(Environment.NewLine, lines);
        UpdateDeviceButtons();
    }

    // Step H4a (§7.2): who sends email, and why nobody does meanwhile.
    private string MasterLine(MasterView view)
    {
        var master = view.Master;
        if (master.Election is not { } election) return _loc.Get("Ui.HouseholdDialog.Master.None");
        if (master.Pending)
        {
            return master.OutgoingDevice is { } outgoing
                ? _loc.Get("Ui.HouseholdDialog.Master.Pending", NameOf(election.DeviceId), NameOf(outgoing))
                : _loc.Get("Ui.HouseholdDialog.Master.PendingNoOutgoing", NameOf(election.DeviceId));
        }
        if (view.LeaseExpired) return _loc.Get("Ui.HouseholdDialog.Master.LeaseExpired");
        return election.DeviceId == view.ThisDevice
            ? _loc.Get("Ui.HouseholdDialog.Master.ThisDevice")
            : _loc.Get("Ui.HouseholdDialog.Master.Other", NameOf(election.DeviceId));
    }

    // §7.3: the active master (or the one still handing over) has not been
    // seen for longer than the lease: the election is a takeover.
    private bool TakeoverFor()
    {
        var current = _master?.Master.ActiveDevice ?? _master?.Master.OutgoingDevice;
        return current is { } device && _lastSeen.TryGetValue(device, out var seen)
            && DateTimeOffset.UtcNow - seen > MasterRules.DefaultLease;
    }

    private string NameOf(Guid device)
        => _deviceNames.TryGetValue(device, out var name) ? name : device.ToString("N")[..8];

    // Step H5a (§9, D-15 option A): the removed device gets no new key; the
    // administrator chooses the new installation passphrase, then may show
    // a code the other devices take the new key with.
    private async Task RemoveDeviceAsync()
    {
        if (SelectedDevice is not { } device || device == _identity?.DeviceId) return;
        if (MessageBox.Show(this, _loc.Get("Ui.HouseholdDialog.RemoveDevice.Confirm", NameOf(device)),
                _loc.Get("Ui.HouseholdDialog.RemoveDevice"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        using var dialog = new SyncPassphraseDialog(_loc, confirm: true, defaultDeviceName: null,
            "Ui.HouseholdDialog.RemoveDevice.Title", "Ui.HouseholdDialog.RemoveDevice.Hint");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var passphrase = dialog.TakePassphrase();
        try
        {
            // Step H5b: the profile groups the device held are rotated too.
            await _household.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<RemoveDevice>()
                    .ExecuteAsync(device, passphrase, CancellationToken.None);
            });
        }
        catch (ProfilesNotHeldException ex)
        {
            var names = await ProfileNamesAsync();
            Error(_loc.Get("Ui.HouseholdDialog.RemoveDevice.NotHeld",
                string.Join(", ", ex.ProfileIds.Select(id => names.GetValueOrDefault(id, id)))));
            return;
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            await RefreshAllAsync();
            return;
        }
        finally
        {
            Array.Clear(passphrase);
        }
        await RefreshAllAsync();
        if (MessageBox.Show(this, _loc.Get("Ui.HouseholdDialog.RemoveDevice.Done"), Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) == DialogResult.Yes)
        {
            await ShowNewKeyCodeAsync();
        }
    }

    // A code with no profile: the devices that need the new key take it.
    private async Task ShowNewKeyCodeAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        var offers = scope.ServiceProvider.GetRequiredService<HouseholdPairingOffers>();
        HouseholdPairingOffer offer;
        try
        {
            offer = await offers.StartAsync([], CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }
        try
        {
            using var dialog = new SyncPairingDialog(_loc, offer.Code.Text, offer.ExpiresAt, TimeProvider.System,
                "Ui.HouseholdDialog.NewKey.CodeHint");
            dialog.ShowDialog(this);
        }
        finally
        {
            Array.Clear(offer.Code.Secret);
            try
            {
                await offers.EndAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Error(_loc.Get("Ui.SyncDialog.Pair.EndFailed", ex.Message));
            }
        }
    }

    // Step H5a: the new key after a removal on another device.
    private async Task NewKeyAsync()
    {
        var passphraseChoice = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.HouseholdDialog.NewKey.Passphrase"), _loc.Get("Ui.HouseholdDialog.NewKey.PassphraseNote"));
        var codeChoice = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.HouseholdDialog.NewKey.Code"), _loc.Get("Ui.HouseholdDialog.NewKey.CodeNote"));
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = _loc.Get("Ui.HouseholdDialog.NewKey.Heading"),
            AllowCancel = true,
        };
        page.Buttons.Add(passphraseChoice);
        page.Buttons.Add(codeChoice);
        page.Buttons.Add(TaskDialogButton.Cancel);
        var choice = TaskDialog.ShowDialog(this, page);

        HouseholdKeySource source;
        if (choice == passphraseChoice)
        {
            using var dialog = new SyncPassphraseDialog(_loc, confirm: false, defaultDeviceName: null,
                "Ui.HouseholdDialog.NewKey", "Ui.HouseholdDialog.NewKey.PassphraseHint");
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            source = new HouseholdKeySource.Passphrase(dialog.TakePassphrase());
        }
        else if (choice == codeChoice)
        {
            using var dialog = new SyncPairingCodeDialog(_loc, defaultDeviceName: null, household: true);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.HouseholdCode is not { } code) return;
            source = new HouseholdKeySource.Code(code);
        }
        else
        {
            return;
        }

        try
        {
            await _household.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<HouseholdSync>().RekeyAsync(source, CancellationToken.None);
            });
            Info(_loc.Get("Ui.HouseholdDialog.NewKey.Done"));
        }
        catch (HouseholdDeviceRemovedException)
        {
            Error(_loc.Get("Ui.HouseholdDialog.Status.Removed"));
        }
        catch (Exception ex)
        {
            Error(ex switch
            {
                CryptographicException when source is HouseholdKeySource.Passphrase => _loc.Get("Ui.HouseholdDialog.NewKey.WrongPassphrase"),
                SyncPairingExpiredException => _loc.Get("Ui.SyncDialog.PairingCode.Expired"),
                CryptographicException => _loc.Get("Ui.SyncDialog.PairingCode.Rejected"),
                _ => ex.Message,
            });
        }
        finally
        {
            if (source is HouseholdKeySource.Passphrase p) Array.Clear(p.Value);
            if (source is HouseholdKeySource.Code c) Array.Clear(c.Value.Secret);
        }
        await RefreshAllAsync();
    }

    private async Task HandoverAsync()
    {
        if (await HandoverWizardForm.ShowIfPendingAsync(this, _scopes, _loc)) await RefreshAllAsync();
    }

    private async Task MakeMasterAsync()
    {
        if (SelectedDevice is not { } device) return;
        if (MessageBox.Show(this, _loc.Get("Ui.HouseholdDialog.MakeMaster.Confirm", NameOf(device)),
                _loc.Get("Ui.HouseholdDialog.MakeMaster"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            await using (var scope = _scopes.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<ElectMaster>().ExecuteAsync(device, CancellationToken.None,
                    TakeoverFor() ? MasterElectionKind.Takeover : MasterElectionKind.Planned);
            }
            await _household.RunNowAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        await RefreshAllAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        _devices.Items.Clear();
        if (!IsPublished) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<HouseholdSync>().ListDevicesAsync(CancellationToken.None);
            _deviceNames = records.ToDictionary(r => r.DeviceId, r => r.Name);
            _lastSeen = records.ToDictionary(r => r.DeviceId, r => r.LastSeen);
            foreach (var record in records)
            {
                var name = record.DeviceId == _identity?.DeviceId
                    ? _loc.Get("Ui.SyncDialog.Devices.ThisDevice", record.Name)
                    : record.Name;
                var row = new ListViewItem(name) { Tag = record.DeviceId };
                row.SubItems.Add(record.Platform);
                row.SubItems.Add(record.AppVersion);
                row.SubItems.Add(record.LastSeen.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                row.SubItems.Add(_master?.Master switch
                {
                    { ActiveDevice: { } active } when active == record.DeviceId => _loc.Get("Ui.HouseholdDialog.Devices.Master"),
                    { Pending: true, Election: { } e } when e.DeviceId == record.DeviceId => _loc.Get("Ui.HouseholdDialog.Devices.Elected"),
                    _ => string.Empty,
                });
                _devices.Items.Add(row);
            }
        }
        catch (Exception ex)
        {
            _devices.Items.Add(new ListViewItem(_loc.Get("Ui.SyncDialog.Devices.Unavailable", ex.Message)));
        }
    }

    private async Task SyncNowAsync()
    {
        await _household.RunNowAsync();
        await RefreshAllAsync();
    }

    private StorageTargetPicker Picker => new(this, _accounts, _loc, Text);

    private async Task PublishAsync()
    {
        if (IsPublished) return;
        var target = await Picker.ChooseAsync();
        if (target is null) return;
        using var dialog = new SyncPassphraseDialog(_loc, confirm: true, Environment.MachineName,
            "Ui.HouseholdDialog.Publish.Title", "Ui.HouseholdDialog.Publish.Hint");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var passphrase = dialog.TakePassphrase();
        try
        {
            await _household.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<HouseholdSync>()
                    .PublishAsync(target, passphrase, dialog.DeviceName, CancellationToken.None);
            });
            Info(_loc.Get("Ui.HouseholdDialog.Publish.Done"));
            await _household.RunNowAsync();
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

    // §6.2: only profiles whose key this device holds can be offered.
    private async Task AddDeviceAsync()
    {
        if (!IsPublished) return;
        List<HouseholdProfileChoice> choices;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var groupKeys = scope.ServiceProvider.GetRequiredService<IProfileGroupKeys>();
            choices = [];
            foreach (var profile in await scope.ServiceProvider.GetRequiredService<HouseholdLog>().ProfilesAsync(CancellationToken.None))
            {
                if (groupKeys.Load(profile.ProfileId) is not { } key) continue;
                CryptographicOperations.ZeroMemory(key.Key);
                choices.Add(new HouseholdProfileChoice(profile.ProfileId, profile.DisplayName));
            }
        }
        if (choices.Count == 0)
        {
            Error(_loc.Get("Ui.HouseholdDialog.AddDevice.NoProfiles"));
            return;
        }
        using var selection = new HouseholdProfilesDialog(_loc, "Ui.HouseholdDialog.AddDevice.Title",
            "Ui.HouseholdDialog.AddDevice.Hint", choices);
        if (selection.ShowDialog(this) != DialogResult.OK) return;

        await using var offerScope = _scopes.CreateAsyncScope();
        var offers = offerScope.ServiceProvider.GetRequiredService<HouseholdPairingOffers>();
        HouseholdPairingOffer offer;
        try
        {
            offer = await offers.StartAsync(selection.SelectedProfileIds, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }
        try
        {
            using var dialog = new SyncPairingDialog(_loc, offer.Code.Text, offer.ExpiresAt, TimeProvider.System,
                "Ui.HouseholdDialog.AddDevice.CodeHint");
            dialog.ShowDialog(this);
        }
        finally
        {
            Array.Clear(offer.Code.Secret);
            try
            {
                await offers.EndAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Error(_loc.Get("Ui.SyncDialog.Pair.EndFailed", ex.Message));
            }
        }
        await RefreshDevicesAsync();
    }

    private async Task JoinAsync()
    {
        if (IsPublished) return;
        if (!_setup && MessageBox.Show(this, _loc.Get("Ui.HouseholdDialog.Join.Confirm"), _loc.Get("Ui.HouseholdDialog.Join"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var codeChoice = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.HouseholdDialog.Join.Code"), _loc.Get("Ui.HouseholdDialog.Join.CodeNote"));
        var passphraseChoice = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.HouseholdDialog.Join.Passphrase"), _loc.Get("Ui.HouseholdDialog.Join.PassphraseNote"));
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = _loc.Get("Ui.HouseholdDialog.Join.Heading"),
            AllowCancel = true,
        };
        page.Buttons.Add(codeChoice);
        page.Buttons.Add(passphraseChoice);
        page.Buttons.Add(TaskDialogButton.Cancel);
        var choice = TaskDialog.ShowDialog(this, page);

        IReadOnlyList<JoinedProfile>? joined = null;
        if (choice == codeChoice) joined = await JoinWithCodeAsync();
        else if (choice == passphraseChoice) joined = await JoinWithPassphraseAsync();
        if (joined is null)
        {
            await RefreshAllAsync();
            return;
        }

        var names = await ProfileNamesAsync();
        var lines = joined.Select(p => _loc.Get($"Ui.HouseholdDialog.Joined.{p.Status}",
            names.GetValueOrDefault(p.ProfileId, p.ProfileId)));
        var installed = joined.Any(p => p.Status == JoinedProfileStatus.Installed);
        Info(_loc.Get("Ui.HouseholdDialog.Join.Result") + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, lines)
            + (installed && !_setup ? Environment.NewLine + Environment.NewLine + _loc.Get("Ui.SyncDialog.Restart") : string.Empty));
        if (installed && _setup)
        {
            _completed = true;
            DialogResult = DialogResult.OK;
            return;
        }
        // A new profile appears in the picker after a restart.
        if (installed)
        {
            _restarter.RestartAndExit();
            return;
        }
        await RefreshAllAsync();
    }

    // Null when cancelled or failed (the error is shown).
    private async Task<IReadOnlyList<JoinedProfile>?> JoinWithCodeAsync()
    {
        using var dialog = new SyncPairingCodeDialog(_loc, Environment.MachineName, household: true);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.HouseholdCode is not { } code) return null;
        try
        {
            var target = await Picker.ForCodeAsync(code.Provider);
            if (target is null) return null;
            var result = await _household.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<JoinInstallation>()
                    .JoinWithCodeAsync(target, code, dialog.DeviceName, CancellationToken.None);
            });
            return result.Profiles;
        }
        catch (Exception ex)
        {
            Error(ex switch
            {
                SyncPairingExpiredException => _loc.Get("Ui.SyncDialog.PairingCode.Expired"),
                CryptographicException => _loc.Get("Ui.SyncDialog.PairingCode.Rejected"),
                _ => ex.Message,
            });
            return null;
        }
        finally
        {
            Array.Clear(code.Secret);
        }
    }

    // §6.3 pages 3–5: the passphrase is tried on each household of the
    // storage; then an administrator approves and selects the profiles.
    private async Task<IReadOnlyList<JoinedProfile>?> JoinWithPassphraseAsync()
    {
        var target = await Picker.ChooseAsync();
        if (target is null) return null;
        IReadOnlyList<Guid> households;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            households = await scope.ServiceProvider.GetRequiredService<HouseholdSync>()
                .ListHouseholdsAsync(target, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return null;
        }
        if (households.Count == 0)
        {
            Error(_loc.Get("Ui.HouseholdDialog.Join.NoHousehold"));
            return null;
        }

        using var dialog = new SyncPassphraseDialog(_loc, confirm: false, Environment.MachineName,
            "Ui.HouseholdDialog.Join.Title", "Ui.HouseholdDialog.Join.Hint");
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        var passphrase = dialog.TakePassphrase();
        try
        {
            var joined = false;
            foreach (var householdId in households)
            {
                try
                {
                    await _household.WhileIdleAsync(async () =>
                    {
                        await using var scope = _scopes.CreateAsyncScope();
                        return await scope.ServiceProvider.GetRequiredService<JoinInstallation>()
                            .JoinHouseholdAsync(target, householdId, passphrase, dialog.DeviceName, CancellationToken.None);
                    });
                    joined = true;
                    break;
                }
                catch (CryptographicException)
                {
                    // Not this household's passphrase.
                }
            }
            if (!joined)
            {
                Error(_loc.Get("Ui.HouseholdDialog.Join.WrongPassphrase"));
                return null;
            }
            return await ApproveAndInstallAsync(passphrase, dialog.DeviceName);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return null;
        }
        finally
        {
            Array.Clear(passphrase);
        }
    }

    private async Task<IReadOnlyList<JoinedProfile>?> ApproveAndInstallAsync(char[] passphrase, string deviceName)
    {
        List<HouseholdProfileChoice> admins;
        List<HouseholdProfileChoice> profiles;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var held = await scope.ServiceProvider.GetRequiredService<HouseholdLog>().ProfilesAsync(CancellationToken.None);
            var escrows = (await scope.ServiceProvider.GetRequiredService<HouseholdKeyring>().KeysAsync(CancellationToken.None)).Escrows;
            admins = [.. held.Where(p => p.Role == HouseholdRole.Admin).Select(p => new HouseholdProfileChoice(p.ProfileId, p.DisplayName))];
            profiles = [.. held.Where(p => escrows.ContainsKey(p.ProfileId)).Select(p => new HouseholdProfileChoice(p.ProfileId, p.DisplayName))];
        }

        while (true)
        {
            using var approval = new HouseholdProfilesDialog(_loc, "Ui.HouseholdDialog.Approve.Title",
                "Ui.HouseholdDialog.Approve.Hint", profiles, admins);
            if (approval.ShowDialog(this) != DialogResult.OK)
            {
                Info(_loc.Get("Ui.HouseholdDialog.Approve.Cancelled"));
                return [];
            }
            try
            {
                return await _household.WhileIdleAsync(async () =>
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var join = scope.ServiceProvider.GetRequiredService<JoinInstallation>();
                    var granted = await join.ApproveAndGrantAsync(approval.AdminProfileId!, approval.Pin,
                        approval.SelectedProfileIds, passphrase, CancellationToken.None);
                    return await join.InstallGrantedAsync(granted, deviceName, CancellationToken.None);
                });
            }
            catch (HouseholdApprovalException ex)
            {
                Error(_loc.Get(ex.Error == HouseholdApprovalError.WrongPin
                    ? "Ui.HouseholdDialog.Approve.WrongPin"
                    : "Ui.HouseholdDialog.Approve.NotAdmin"));
            }
        }
    }

    private async Task<Dictionary<string, string>> ProfileNamesAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<HouseholdLog>().ProfilesAsync(CancellationToken.None))
            .ToDictionary(p => p.ProfileId, p => p.DisplayName, StringComparer.Ordinal);
    }

    private Button Action(string key, Func<Task> action)
    {
        var button = new Button { Text = _loc.Get(key), AutoSize = true, Height = 32 };
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
                // Closed by the application (exit, restart) while the action ran.
            }
            finally
            {
                _busy--;
                if (!IsDisposed)
                {
                    button.Enabled = true;
                    UpdateBusy();
                    _addDevice.Enabled = IsPublished;
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

    private void Info(string message)
    {
        if (!IsDisposed) MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Error(string message)
    {
        if (!IsDisposed) MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
