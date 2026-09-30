using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Domain.Household;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.UI.Forms;

// The handover wizard of the elected device (household step H4b;
// docs/analysis/ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §7.2 step 3, §7.3,
// D-18). One window, top to bottom:
//
//   - the inherited installation settings (SMTP, cloud backup, reference
//     country), shown; they are edited in Tools → Settings as usual;
//   - the SMTP connection test, on this device;
//   - the cloud-backup storage sign-in on this device and the cloud-backup
//     passphrase, typed again (it is never replicated);
//   - the profiles: those granted are downloaded, those no device at hand
//     holds are recovered with the installation passphrase;
//   - Confirm: the next household run activates this device as master
//     (MasterRules still wait for the outgoing master or its lease).
//
// Later closes the window; the installation window offers it again.
internal sealed class HandoverWizardForm : MedReminderFormBase
{
    private readonly IServiceScopeFactory _scopes;
    private readonly HouseholdHostedService _household;
    private readonly ICloudAccountService _accounts;
    private readonly ICloudBackupPassphraseStore _cloudPassphrase;
    private readonly IEmailNotificationService _email;
    private readonly ILocalizationService _loc;
    private readonly HandoverView _view;
    private readonly BackupSettings _backup;

    private readonly Label _smtpResult;
    private readonly Label _cloudResult;
    private readonly TextBox? _cloudPassphraseBox;
    private readonly TextBox? _householdPassphraseBox;
    private readonly Button _confirm;
    private int _busy;

    public HandoverWizardForm(IServiceScopeFactory scopes, HouseholdHostedService household, ICloudAccountService accounts,
        ICloudBackupPassphraseStore cloudPassphrase, IEmailNotificationService email, IInstallationSettingsStore settings,
        ILocalizationService localization, HandoverView view)
    {
        _scopes = scopes;
        _household = household;
        _accounts = accounts;
        _cloudPassphrase = cloudPassphrase;
        _email = email;
        _loc = localization;
        _view = view;
        _backup = settings.ReadBackup();
        var smtp = settings.ReadSmtp();
        var user = settings.ReadUser();
        var takeover = view.Election.Kind == MasterElectionKind.Takeover;

        Text = _loc.Get(takeover ? "Ui.HandoverWizard.TitleTakeover" : "Ui.HandoverWizard.Title");
        Width = 640;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        var width = LogicalToDeviceUnits(580);

        Add(layout, Text2(_loc.Get(takeover ? "Ui.HandoverWizard.IntroTakeover" : "Ui.HandoverWizard.Intro"), width));

        // Inherited settings.
        Add(layout, Heading(_loc.Get("Ui.HandoverWizard.Settings")));
        var summary = new List<string>
        {
            string.IsNullOrWhiteSpace(smtp.Host)
                ? _loc.Get("Ui.HandoverWizard.Settings.NoSmtp")
                : _loc.Get("Ui.HandoverWizard.Settings.Smtp", smtp.Host, smtp.Port, smtp.FromAddress),
            _backup.CloudFolderEnabled && _backup.CloudProvider is { } provider
                ? _loc.Get("Ui.HandoverWizard.Settings.Cloud", provider, _backup.CloudFolderRetention)
                : _loc.Get("Ui.HandoverWizard.Settings.NoCloud"),
            _loc.Get("Ui.HandoverWizard.Settings.Country", user.ReferenceCountry),
            _loc.Get("Ui.HandoverWizard.Settings.Edit"),
        };
        Add(layout, Text2(string.Join(Environment.NewLine, summary), width));

        // SMTP test.
        _smtpResult = new Label { AutoSize = true, MaximumSize = new Size(width, 0) };
        if (!string.IsNullOrWhiteSpace(smtp.Host))
        {
            var test = new Button { Text = _loc.Get("Ui.HandoverWizard.SmtpTest"), AutoSize = true, Height = 32 };
            test.Click += async (_, _) => await RunAsync(test, TestSmtpAsync);
            Add(layout, test);
            Add(layout, _smtpResult);
        }

        // Cloud backup: sign-in and passphrase on this device (D-18).
        _cloudResult = new Label { AutoSize = true, MaximumSize = new Size(width, 0) };
        if (_backup.CloudFolderEnabled && _backup.CloudProvider is { } cloud)
        {
            Add(layout, Heading(_loc.Get("Ui.HandoverWizard.Cloud")));
            var signIn = new Button { Text = _loc.Get("Ui.HandoverWizard.CloudSignIn", cloud), AutoSize = true, Height = 32 };
            signIn.Click += async (_, _) => await RunAsync(signIn, () => SignInAsync(cloud));
            Add(layout, signIn);
            Add(layout, _cloudResult);
            Add(layout, Text2(_loc.Get(_cloudPassphrase.HasPassphrase
                ? "Ui.HandoverWizard.CloudPassphraseSet"
                : "Ui.HandoverWizard.CloudPassphrase"), width));
            _cloudPassphraseBox = new TextBox { UseSystemPasswordChar = true, Width = width };
            Add(layout, _cloudPassphraseBox);
        }

        // Profiles.
        Add(layout, Heading(_loc.Get("Ui.HandoverWizard.Profiles")));
        Add(layout, Text2(string.Join(Environment.NewLine, view.Profiles.Select(p =>
            _loc.Get($"Ui.HandoverWizard.Profile.{p.State}", p.DisplayName))), width));
        if (view.Profiles.Any(p => p.State == HandoverProfileState.Missing))
        {
            Add(layout, Text2(_loc.Get("Ui.HandoverWizard.Recover"), width));
            _householdPassphraseBox = new TextBox { UseSystemPasswordChar = true, Width = width };
            Add(layout, _householdPassphraseBox);
        }

        _confirm = new Button { Text = _loc.Get("Ui.HandoverWizard.Confirm"), AutoSize = true, Height = 32 };
        _confirm.Click += async (_, _) => await RunAsync(_confirm, ConfirmAsync);
        var later = new Button { Text = _loc.Get("Ui.HandoverWizard.Later"), DialogResult = DialogResult.Cancel, AutoSize = true, Height = 32 };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(later);
        buttons.Controls.Add(_confirm);

        Controls.Add(layout);
        Controls.Add(buttons);
        CancelButton = later;
        FormClosing += (_, e) =>
        {
            if (_busy > 0 && e.CloseReason is CloseReason.UserClosing or CloseReason.None && DialogResult != DialogResult.OK)
                e.Cancel = true;
        };
    }

    // The pending handover of this device, or null. Shows the wizard and
    // returns true when the administrator confirmed it.
    public static async Task<bool> ShowIfPendingAsync(IWin32Window owner, IServiceScopeFactory scopes,
        ILocalizationService localization)
    {
        HandoverView? view;
        await using (var scope = scopes.CreateAsyncScope())
        {
            view = await scope.ServiceProvider.GetRequiredService<MasterHandover>().PendingAsync(CancellationToken.None);
        }
        if (view is null) return false;
        await using var dialogScope = scopes.CreateAsyncScope();
        var sp = dialogScope.ServiceProvider;
        using var wizard = new HandoverWizardForm(scopes, sp.GetRequiredService<HouseholdHostedService>(),
            sp.GetRequiredService<ICloudAccountService>(), sp.GetRequiredService<ICloudBackupPassphraseStore>(),
            sp.GetRequiredService<IEmailNotificationService>(), sp.GetRequiredService<IInstallationSettingsStore>(),
            localization, view);
        return wizard.ShowDialog(owner) == DialogResult.OK;
    }

    private async Task TestSmtpAsync()
    {
        bool ok;
        try
        {
            ok = await _email.TestConnectionAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            ok = false;
        }
        _smtpResult.Text = _loc.Get(ok ? "Ui.HandoverWizard.SmtpOk" : "Ui.HandoverWizard.SmtpFailed");
    }

    private async Task SignInAsync(CloudProvider provider)
    {
        var accountId = string.IsNullOrEmpty(_backup.CloudAccountId) ? null : _backup.CloudAccountId;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var account = await _accounts.SignInAsync(provider, accountId, timeout.Token);
            _cloudResult.Text = accountId is not null && account.Id != accountId
                ? _loc.Get("Ui.HandoverWizard.CloudOtherAccount")
                : _loc.Get("Ui.HandoverWizard.CloudSignedIn", account.UserName);
        }
        catch (Exception ex)
        {
            _cloudResult.Text = _loc.Get(StorageTargetPicker.ProviderKey("Ui.SyncDialog.SignIn.Failed", provider), ex.Message);
        }
    }

    private async Task ConfirmAsync()
    {
        if (_cloudPassphraseBox is not null && !_cloudPassphrase.HasPassphrase
            && string.IsNullOrWhiteSpace(_cloudPassphraseBox.Text))
        {
            Warn(_loc.Get("Ui.HandoverWizard.CloudPassphraseRequired"));
            return;
        }
        if (_cloudPassphraseBox is { Text.Length: > 0 })
        {
            var cloud = _cloudPassphraseBox.Text.ToCharArray();
            try
            {
                _cloudPassphrase.SetPassphrase(cloud);
            }
            finally
            {
                Array.Clear(cloud);
                _cloudPassphraseBox.Text = string.Empty;
            }
        }

        var lines = new List<string>();
        var passphrase = _householdPassphraseBox is { Text.Length: > 0 } box ? box.Text.ToCharArray() : null;
        if (_householdPassphraseBox is not null) _householdPassphraseBox.Text = string.Empty;
        try
        {
            var joined = await _household.WhileIdleAsync(async () =>
            {
                await using var scope = _scopes.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var deviceName = (await sp.GetRequiredService<IHouseholdStore>().EnsureCreatedAsync(CancellationToken.None))
                    .DeviceName ?? Environment.MachineName;
                var ids = _view.Profiles.Where(p => p.State == HandoverProfileState.Granted).Select(p => p.ProfileId).ToList();
                if (passphrase is not null)
                {
                    var missing = _view.Profiles.Where(p => p.State == HandoverProfileState.Missing).Select(p => p.ProfileId).ToList();
                    ids.AddRange(await sp.GetRequiredService<HouseholdSync>()
                        .GrantFromEscrowAsync(missing, passphrase, CancellationToken.None));
                }
                var installed = await sp.GetRequiredService<JoinInstallation>()
                    .InstallGrantedAsync(ids, deviceName, CancellationToken.None);
                await sp.GetRequiredService<MasterHandover>().ConfirmAsync(_view.Election.ElectionId, CancellationToken.None);
                return installed;
            });
            var names = _view.Profiles.ToDictionary(p => p.ProfileId, p => p.DisplayName, StringComparer.Ordinal);
            lines.AddRange(joined.Select(p => _loc.Get($"Ui.HouseholdDialog.Joined.{p.Status}",
                names.GetValueOrDefault(p.ProfileId, p.ProfileId))));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Warn(_loc.Get("Ui.HouseholdDialog.Join.WrongPassphrase"));
            return;
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            return;
        }
        finally
        {
            if (passphrase is not null) Array.Clear(passphrase);
        }

        await _household.RunNowAsync();
        MessageBox.Show(this, _loc.Get("Ui.HandoverWizard.Done") + (lines.Count == 0 ? string.Empty
                : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines)),
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
    }

    private async Task RunAsync(Button button, Func<Task> action)
    {
        button.Enabled = false;
        _busy++;
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            // Closed by the application while the action ran.
        }
        finally
        {
            _busy--;
            if (!IsDisposed)
            {
                button.Enabled = true;
                UseWaitCursor = _busy > 0;
            }
        }
    }

    private void Warn(string message)
    {
        if (!IsDisposed) MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void Add(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control);
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        Margin = new Padding(3, 12, 3, 4),
    };

    private static Label Text2(string text, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Margin = new Padding(3, 3, 3, 6),
    };
}
