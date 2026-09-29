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
    private readonly Button _addDevice;
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
        Font = new Font("Segoe UI", 9.75F);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var statusPage = new TabPage(_loc.Get("Ui.HouseholdDialog.Tab.Status")) { Padding = new Padding(12) };
        _statusText = new Label { Dock = DockStyle.Fill, AutoSize = false };
        _publish = Action("Ui.HouseholdDialog.Publish", PublishAsync);
        _join = Action("Ui.HouseholdDialog.Join", JoinAsync);
        _syncNow = Action("Ui.SyncDialog.SyncNow", SyncNowAsync);
        var statusButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        statusButtons.Controls.AddRange([_syncNow, _publish, _join]);
        statusPage.Controls.Add(_statusText);
        statusPage.Controls.Add(statusButtons);

        var devicesPage = new TabPage(_loc.Get("Ui.HouseholdDialog.Tab.Devices")) { Padding = new Padding(12) };
        _devices = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill };
        foreach (var (key, width) in new[]
                 {
                     ("Ui.SyncDialog.Devices.Name", 240), ("Ui.SyncDialog.Devices.Platform", 110),
                     ("Ui.SyncDialog.Devices.Version", 110), ("Ui.SyncDialog.Devices.LastSeen", 170),
                 })
        {
            _devices.Columns.Add(_loc.Get(key), width);
        }
        _addDevice = Action("Ui.HouseholdDialog.AddDevice", AddDeviceAsync);
        var deviceButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        deviceButtons.Controls.Add(_addDevice);
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
    }

    private async Task RefreshStatusAsync()
    {
        await using var scope = _scopes.CreateAsyncScope();
        _identity = await scope.ServiceProvider.GetRequiredService<IHouseholdStore>().EnsureCreatedAsync(CancellationToken.None);
        // No profile database is open during the first-run join.
        var link = _setup
            ? new HouseholdLinkStatus(HouseholdLinkState.None, null)
            : await scope.ServiceProvider.GetRequiredService<HouseholdLinks>().StatusAsync(CancellationToken.None);
        if (IsDisposed) return;

        _publish.Visible = !IsPublished && !_setup;
        _join.Visible = !IsPublished;
        _syncNow.Visible = IsPublished;
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
        }
        // §11: this profile's group was claimed first by another installation.
        if (link.State == HouseholdLinkState.OtherHousehold) lines.Add(_loc.Get("Ui.HouseholdDialog.Status.LinkedElsewhere"));
        _statusText.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task RefreshDevicesAsync()
    {
        _devices.Items.Clear();
        if (!IsPublished) return;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<HouseholdSync>().ListDevicesAsync(CancellationToken.None);
            foreach (var record in records)
            {
                var name = record.DeviceId == _identity?.DeviceId
                    ? _loc.Get("Ui.SyncDialog.Devices.ThisDevice", record.Name)
                    : record.Name;
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
