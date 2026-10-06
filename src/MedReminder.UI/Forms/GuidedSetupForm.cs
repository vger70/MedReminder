using MedReminder.Application.Abstractions;
using MedReminder.Application.GuidedSetup;
using MedReminder.Application.Notifications;
using MedReminder.Application.Overview;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Settings;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MedReminder.UI.Forms;

// What the guided setup opens in the main window: the existing windows,
// owned by the setup so they stay above it.
internal sealed record GuidedSetupHost(
    // The medicine dialog in Create mode; the new medicine or null.
    Func<IWin32Window, Task<Guid?>> AddMedicine,
    // Every medicine of the last load of the main list.
    Func<IReadOnlyList<MedicineListItem>> Medicines,
    Action<IWin32Window, SettingsSection> OpenSettings,
    Func<IWin32Window, Task> OpenDoseTimes,
    // Null for a standard user: the installation window is for administrators.
    Action<IWin32Window>? OpenInstallation,
    Func<bool> SmtpConfigured);

// Guided setup (docs/prompt/PROMPT-GUIDED-SETUP.md): one window with a
// step indicator and Back / Next / Not now. The steps and their order
// come from GuidedSetupFlow; this form shows them and writes each answer
// when the user leaves its step with Next, through the existing use
// cases: RenameProfile, ApplyGuidedSetupWarning (UpdateMedicine),
// GuidedSetupEmail (UpdateNotificationSettings). The lead time and
// channels for new medicines go in the profile's ui.settings.json,
// device-local. Esc and closing the window are "Not now".
internal sealed class GuidedSetupForm : MedReminderFormBase
{
    private const int TextWidth = 560;

    private readonly GuidedSetupFlow _flow;
    private readonly ILocalizationService _loc;
    private readonly IServiceScopeFactory _scopes;
    private readonly GuidedSetupHost _host;
    private readonly ICurrentProfile _profile;
    private readonly ILogger _log;

    private readonly Label _stepLabel;
    private readonly Panel _pageHost;
    private readonly Dictionary<GuidedSetupStep, Control> _pages = new();
    private readonly Button _next;
    private readonly Button _back;
    private readonly Button _notNow;
    private int _busy;

    // Step 1.
    private readonly RadioButton _forMe;
    private readonly RadioButton _forSomeoneElse;
    private readonly TextBox _nameBox;
    private readonly Label _nameError;
    private string _savedName;

    // Step 2.
    private readonly Label _medicinesIntro;
    private readonly ListView _medicineList;
    private readonly Label _noMedicine;

    // Step 3.
    private readonly List<(RadioButton Button, int Days)> _presets = [];
    private readonly RadioButton _customLead;
    private readonly NumericUpDown _customDays;
    private readonly RadioButton _channelWindows;
    private readonly RadioButton _channelEmail;
    private readonly RadioButton _channelBoth;

    // Step 4.
    private readonly Label _emailIntro;
    private readonly Label _userAddressLabel;
    private readonly Label _otherAddressLabel;
    private readonly TextBox _userAddress;
    private readonly TextBox _otherAddress;
    private readonly Label _addressError;
    private readonly Dictionary<EmailKind, CheckBox> _caregiverKinds = new();
    private readonly CheckBox _weeklyDigest;
    private readonly Label _accountState;
    private readonly Button _setUpAccount;
    private readonly Label _sentByMaster;
    private GuidedSetupEmailSettings _savedEmail;
    private GuidedSetupAudience? _emailFieldsFor;

    // Step 5.
    private readonly Label _summary;
    private readonly Label _noOneWarned;
    private readonly Button _addWindows;

    public GuidedSetupForm(GuidedSetupFlow flow, GuidedSetupEmailSettings email, string profileName,
        ILocalizationService localization, IServiceScopeFactory scopes, ICurrentProfile profile,
        GuidedSetupHost host, ILogger logger)
    {
        _flow = flow;
        _savedEmail = email;
        _savedName = profileName;
        _loc = localization;
        _scopes = scopes;
        _profile = profile;
        _host = host;
        _log = logger;

        Text = _loc.Get("Ui.GuidedSetup.Title");
        Width = 680;
        Height = 600;
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        _stepLabel = new Label
        {
            Dock = DockStyle.Top,
            Font = UiTheme.Fonts.Heading(),
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.S),
        };
        DialogLayout.GrowWithText(_stepLabel);
        _pageHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };

        // Step 1: who the medicines are for, and the profile name.
        _forMe = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Audience.Myself"), Checked = true };
        _forSomeoneElse = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Audience.SomeoneElse") };
        _nameBox = new TextBox
        {
            Width = 320,
            MaxLength = 100,
            Text = profileName,
            AccessibleName = _loc.Get("Ui.GuidedSetup.Audience.ProfileName"),
            // Renaming is an administrator's action, as in Manage profiles.
            ReadOnly = !profile.IsAdmin,
        };
        _nameError = DialogLayout.ErrorLabel();
        AddPage(GuidedSetupStep.Audience,
            Paragraph(_loc.Get("Ui.GuidedSetup.Audience.Intro")),
            Paragraph(_loc.Get("Ui.GuidedSetup.Audience.Question")),
            Indented(_forMe),
            Indented(_forSomeoneElse),
            Paragraph(_loc.Get("Ui.GuidedSetup.Audience.ProfileName")),
            _nameBox,
            _nameError,
            Hint(_loc.Get(_profile.IsAdmin
                ? "Ui.GuidedSetup.Audience.MoreProfilesAdmin"
                : "Ui.GuidedSetup.Audience.MoreProfilesUser")));

        // Step 2: the medicines, through the existing medicine dialog.
        _medicinesIntro = Paragraph(string.Empty);
        var addMedicine = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Medicines.Add"));
        addMedicine.Margin = new Padding(0, 0, 0, UiTheme.Space.S);
        addMedicine.Click += async (_, _) => await RunAsync(AddMedicineAsync);
        _medicineList = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Width = TextWidth,
            Height = 160,
            AccessibleName = _loc.Get("Ui.GuidedSetup.Medicines.Column.Name"),
        };
        _medicineList.Columns.Add(_loc.Get("Ui.GuidedSetup.Medicines.Column.Name"), 400);
        _medicineList.Columns.Add(_loc.Get("Ui.GuidedSetup.Medicines.Column.DaysLeft"), 140);
        _noMedicine = Hint(_loc.Get("Ui.GuidedSetup.Medicines.None"));
        AddPage(GuidedSetupStep.Medicines, _medicinesIntro, addMedicine, _noMedicine, _medicineList);

        // Step 3: lead time and channel.
        var leadRows = new List<Control> { Paragraph(_loc.Get("Ui.GuidedSetup.Warning.LeadQuestion")) };
        foreach (var days in GuidedSetupFlow.LeadTimePresets)
        {
            var preset = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Warning.Days", days) };
            _presets.Add((preset, days));
            leadRows.Add(Indented(preset));
        }
        _customLead = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Warning.Custom") };
        _customDays = new NumericUpDown
        {
            Minimum = NewMedicineDefaults.MinThresholdDays,
            Maximum = NewMedicineDefaults.MaxThresholdDays,
            Width = 80,
            AccessibleName = _loc.Get("Ui.GuidedSetup.Warning.Custom"),
        };
        var customRow = DialogLayout.Row(_customLead, _customDays);
        customRow.Padding = new Padding(UiTheme.Space.L, 0, 0, 0);
        leadRows.Add(customRow);
        _channelWindows = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Warning.Channel.Windows") };
        _channelEmail = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Warning.Channel.Email") };
        _channelBoth = new RadioButton { AutoSize = true, Text = _loc.Get("Ui.GuidedSetup.Warning.Channel.Both") };
        // The two groups of radio buttons sit in panels of their own, or
        // they would form one group.
        var leadGroup = Group(leadRows.ToArray());
        var channelGroup = Group(
            Paragraph(_loc.Get("Ui.GuidedSetup.Warning.ChannelQuestion")),
            Indented(_channelWindows),
            Indented(_channelEmail),
            Indented(_channelBoth));
        AddPage(GuidedSetupStep.Warning, leadGroup, channelGroup, Hint(_loc.Get("Ui.GuidedSetup.Warning.Scope")));

        // Step 4: addresses, caregiver copies, email account.
        _emailIntro = Paragraph(string.Empty);
        _userAddressLabel = Paragraph(string.Empty);
        _otherAddressLabel = Paragraph(string.Empty);
        _userAddress = new TextBox { Width = 360, MaxLength = 254 };
        _otherAddress = new TextBox { Width = 360, MaxLength = 254 };
        _addressError = DialogLayout.ErrorLabel();
        var kindRows = new List<Control> { Paragraph(_loc.Get("Ui.SettingsDialog.Notifications.Caregiver.Kinds")) };
        foreach (var kind in CaregiverEmails.Choices)
        {
            var box = new CheckBox
            {
                AutoSize = true,
                Text = _loc.Get("Ui.SettingsDialog.Notifications.Caregiver.Kind." + kind),
            };
            _caregiverKinds[kind] = box;
            kindRows.Add(Indented(box));
        }
        _weeklyDigest = new CheckBox
        {
            AutoSize = true,
            Text = _loc.Get("Ui.SettingsDialog.Notifications.Caregiver.Digest"),
        };
        kindRows.Add(_weeklyDigest);
        _accountState = Paragraph(string.Empty);
        _setUpAccount = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Email.SetUpAccount"));
        _setUpAccount.Margin = new Padding(0, 0, 0, UiTheme.Space.S);
        _setUpAccount.Click += (_, _) => SetUpEmailAccount();
        _sentByMaster = Hint(_loc.Get("Ui.GuidedSetup.Email.SentByMaster"));
        AddPage(GuidedSetupStep.Email,
            _emailIntro,
            _userAddressLabel, _userAddress,
            _otherAddressLabel, _otherAddress,
            _addressError,
            Group(kindRows.ToArray()),
            _accountState, _setUpAccount, _sentByMaster);

        // Step 5: what the app will do, and optional next steps.
        _summary = Paragraph(string.Empty);
        _noOneWarned = Paragraph(_loc.Get("Ui.GuidedSetup.Summary.NoOneWarned"));
        _noOneWarned.ForeColor = UiTheme.Palette.DangerText;
        _addWindows = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Summary.AddWindows"));
        _addWindows.Margin = new Padding(0, 0, 0, UiTheme.Space.S);
        _addWindows.Click += async (_, _) => await RunAsync(AddWindowsChannelAsync);
        var nextSteps = new List<Control>
        {
            Paragraph(_loc.Get("Ui.GuidedSetup.Summary.NextSteps")),
            Link("Ui.GuidedSetup.Summary.DoseTimes", async () => await _host.OpenDoseTimes(this)),
            Link("Ui.GuidedSetup.Summary.Backup", () => { _host.OpenSettings(this, SettingsSection.Backup); return Task.CompletedTask; }),
        };
        if (_host.OpenInstallation is { } openInstallation)
        {
            nextSteps.Add(Link("Ui.GuidedSetup.Summary.OtherComputer",
                () => { openInstallation(this); return Task.CompletedTask; }));
        }
        AddPage(GuidedSetupStep.Summary,
            _summary, _noOneWarned, _addWindows, Group(nextSteps.ToArray()));

        _next = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Button.Next"));
        _next.Click += async (_, _) => await RunAsync(NextAsync);
        _back = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Button.Back"));
        _back.Click += (_, _) => GoBack();
        _notNow = DialogLayout.Button(_loc.Get("Ui.GuidedSetup.Button.NotNow"), DialogResult.Cancel);
        var buttons = DialogLayout.ButtonBar(this, _next, _notNow, _back);
        DialogLayout.KeepButtonsVisible(this, buttons);

        Controls.Add(_pageHost);
        Controls.Add(_stepLabel);
        Controls.Add(buttons);

        LoadAnswers();
        ShowStep();

        FormClosing += (_, e) =>
        {
            // An answer being saved finishes first; otherwise every way
            // out but Finish is "Not now". Finish sets OK while it runs.
            if (_busy > 0 && e.CloseReason is CloseReason.UserClosing or CloseReason.None
                && DialogResult != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
            _flow.Dismiss();
        };
    }

    // The profile name after the setup, when step 1 renamed it.
    public string? RenamedTo { get; private set; }

    // Enter is Next, except on a control that uses Enter itself: a link,
    // the medicine list (the focused button is clicked by the base class).
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter && ActiveControl is LinkLabel or ListView) return false;
        return base.ProcessDialogKey(keyData);
    }

    private void LoadAnswers()
    {
        _forSomeoneElse.Checked = _flow.Audience == GuidedSetupAudience.SomeoneElse;
        _customDays.Value = _flow.LeadDays;
        (_presets.FirstOrDefault(p => p.Days == _flow.LeadDays).Button ?? _customLead).Checked = true;
        // Typing a number picks the custom lead time.
        _customDays.ValueChanged += (_, _) => _customLead.Checked = true;
        (_flow.Channels switch
        {
            NotificationChannels.Email => _channelEmail,
            NotificationChannels.Both => _channelBoth,
            _ => _channelWindows,
        }).Checked = true;
        var copied = _savedEmail.CaregiverEmails;
        foreach (var (kind, box) in _caregiverKinds) box.Checked = copied.Contains(kind);
        _weeklyDigest.Checked = _savedEmail.WeeklyDigest;
    }

    private void ShowStep()
    {
        var step = _flow.Current;
        switch (step)
        {
            case GuidedSetupStep.Medicines:
                _medicinesIntro.Text = _loc.Get(_flow.Audience == GuidedSetupAudience.Myself
                    ? "Ui.GuidedSetup.Medicines.IntroMyself"
                    : "Ui.GuidedSetup.Medicines.IntroSomeoneElse");
                RefreshMedicines();
                break;
            case GuidedSetupStep.Email:
                PrepareEmailStep();
                break;
            case GuidedSetupStep.Summary:
                _flow.SetSmtpConfigured(_host.SmtpConfigured());
                RefreshSummary();
                break;
        }

        _stepLabel.Text = _loc.Get("Ui.GuidedSetup.Step", _flow.CurrentIndex + 1, _flow.Steps.Count,
            _loc.Get("Ui.GuidedSetup.StepName." + step));
        AccessibleDescription = _stepLabel.Text;
        _pageHost.SuspendLayout();
        foreach (var (pageStep, page) in _pages) page.Visible = pageStep == step;
        _pageHost.ResumeLayout(performLayout: true);
        _back.Enabled = _flow.CanGoBack;
        _next.Text = _loc.Get(_flow.IsLastStep ? "Ui.GuidedSetup.Button.Finish" : "Ui.GuidedSetup.Button.Next");
        FocusFirst(_pages[step]);
    }

    private void FocusFirst(Control page)
    {
        var first = page.Controls.Count == 0 ? null : FirstFocusable(page);
        if (first is not null) ActiveControl = first;
        else ActiveControl = _next;
    }

    private static Control? FirstFocusable(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible || !child.Enabled) continue;
            if (child.CanSelect && child.TabStop && child is not Label) return child;
            if (child.HasChildren && FirstFocusable(child) is { } nested) return nested;
        }
        return null;
    }

    private async Task NextAsync()
    {
        if (!await SaveCurrentStepAsync()) return;
        if (_flow.IsLastStep)
        {
            _flow.Finish();
            DialogResult = DialogResult.OK;
            return;
        }
        _flow.Next();
        ShowStep();
    }

    private void GoBack()
    {
        if (_busy > 0 || !_flow.CanGoBack) return;
        // Back keeps the answers of the step on screen without saving
        // them; they are saved when the user leaves the step with Next.
        ReadWarningAnswers();
        _flow.Back();
        ShowStep();
    }

    // Saves the answers of the step being left. False keeps the user on
    // the step (an inline error or a message says why).
    private async Task<bool> SaveCurrentStepAsync()
    {
        switch (_flow.Current)
        {
            case GuidedSetupStep.Audience:
                _flow.Audience = _forSomeoneElse.Checked ? GuidedSetupAudience.SomeoneElse : GuidedSetupAudience.Myself;
                return await SaveProfileNameAsync();
            case GuidedSetupStep.Warning:
                ReadWarningAnswers();
                return await SaveWarningAsync();
            case GuidedSetupStep.Email:
                return await SaveEmailAsync();
            default:
                return true;
        }
    }

    private async Task<bool> SaveProfileNameAsync()
    {
        var name = _nameBox.Text.Trim();
        if (!_profile.IsAdmin) return true;
        if (name.Length == 0)
        {
            DialogLayout.ShowError(_nameError, _loc.Get("Ui.GuidedSetup.Audience.ProfileNameEmpty"), _nameBox);
            return false;
        }
        DialogLayout.ShowError(_nameError, null);
        if (string.Equals(name, _savedName, StringComparison.Ordinal)) return true;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RenameProfile>()
                .ExecuteAsync(_profile.Id, name, CancellationToken.None);
            _savedName = name;
            RenamedTo = name;
            return true;
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    private void ReadWarningAnswers()
    {
        var preset = _presets.FirstOrDefault(p => p.Button.Checked);
        _flow.LeadDays = preset.Button is not null ? preset.Days : (int)_customDays.Value;
        _flow.Channels = _channelBoth.Checked ? NotificationChannels.Both
            : _channelEmail.Checked ? NotificationChannels.Email
            : NotificationChannels.Windows;
    }

    // The warning on the medicines added in this setup (replicated, as
    // any medicine edit) and as the start of new medicines on this device.
    private async Task<bool> SaveWarningAsync()
    {
        try
        {
            if (_flow.AddedMedicines.Count > 0)
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApplyGuidedSetupWarning>()
                    .ExecuteAsync(_flow.AddedMedicines, _flow.Defaults, CancellationToken.None);
            }
            ProfileUiSettingsFile.WriteNewMedicineDefaults(_profile.DataDirectory, _flow.Defaults);
            return true;
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    private void PrepareEmailStep()
    {
        // The fields keep what the user typed while the audience stays;
        // a change of audience on step 1 fills them again from the saved
        // settings, so each address lands in its field.
        if (_emailFieldsFor == _flow.Audience)
        {
            RefreshAccountState();
            return;
        }
        _emailFieldsFor = _flow.Audience;
        var myself = _flow.Audience == GuidedSetupAudience.Myself;
        _emailIntro.Text = _loc.Get(myself
            ? "Ui.GuidedSetup.Email.IntroMyself"
            : "Ui.GuidedSetup.Email.IntroSomeoneElse");
        _userAddressLabel.Text = _loc.Get("Ui.GuidedSetup.Email.User");
        _otherAddressLabel.Text = _loc.Get(myself
            ? "Ui.GuidedSetup.Email.OtherMyself"
            : "Ui.GuidedSetup.Email.OtherSomeoneElse");
        _userAddress.AccessibleName = _userAddressLabel.Text;
        _otherAddress.AccessibleName = _otherAddressLabel.Text;
        var (user, other) = _flow.AddressFields(_savedEmail.Addresses);
        _userAddress.Text = user;
        _otherAddress.Text = other;
        DialogLayout.ShowError(_addressError, null);
        RefreshAccountState();
    }

    private void RefreshAccountState()
    {
        _flow.SetSmtpConfigured(_host.SmtpConfigured());
        _accountState.Text = _loc.Get(_flow.EmailAccount switch
        {
            EmailAccountState.Configured => "Ui.GuidedSetup.Email.SmtpReady",
            EmailAccountState.AdministratorCanSetUp => "Ui.GuidedSetup.Email.SmtpMissingAdmin",
            _ => "Ui.GuidedSetup.Email.SmtpMissingUser",
        });
        _setUpAccount.Visible = _flow.EmailAccount == EmailAccountState.AdministratorCanSetUp;
        _sentByMaster.Visible = !_flow.SendsEmail;
    }

    private void SetUpEmailAccount()
    {
        if (_busy > 0) return;
        _host.OpenSettings(this, SettingsSection.Email);
        if (!IsDisposed) RefreshAccountState();
    }

    private async Task<bool> SaveEmailAsync()
    {
        var addresses = _flow.MapAddresses(_userAddress.Text, _otherAddress.Text);
        var user = _userAddress.Text.Trim();
        var other = _otherAddress.Text.Trim();
        if (user.Length > 0 && !IsMailbox(user))
        {
            DialogLayout.ShowError(_addressError, _loc.Get("Ui.GuidedSetup.Email.Invalid"), _userAddress);
            return false;
        }
        if (other.Length > 0 && !IsMailbox(other))
        {
            DialogLayout.ShowError(_addressError, _loc.Get("Ui.GuidedSetup.Email.Invalid"), _otherAddress);
            return false;
        }
        if (other.Length > 0 && string.Equals(user, other, StringComparison.OrdinalIgnoreCase))
        {
            DialogLayout.ShowError(_addressError, _loc.Get("Ui.GuidedSetup.Email.Same"), _otherAddress);
            return false;
        }
        DialogLayout.ShowError(_addressError, null);

        var wanted = new GuidedSetupEmailSettings(
            addresses,
            _caregiverKinds.Where(k => k.Value.Checked).Select(k => k.Key).ToHashSet(),
            _weeklyDigest.Checked);
        if (wanted.Addresses == _savedEmail.Addresses
            && wanted.WeeklyDigest == _savedEmail.WeeklyDigest
            && wanted.CaregiverEmails.SetEquals(_savedEmail.CaregiverEmails))
        {
            return true;
        }
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<GuidedSetupEmail>()
                .SaveAsync(wanted, CancellationToken.None);
            _savedEmail = wanted;
            return true;
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    // The same rule as Settings → Notifications: a full mailbox with a
    // domain (SettingsDialog._addressParserOptions).
    private static readonly MimeKit.ParserOptions AddressParserOptions = new()
    {
        AllowAddressesWithoutDomain = false,
    };

    private static bool IsMailbox(string address)
        => MimeKit.MailboxAddress.TryParse(AddressParserOptions, address, out _);

    private async Task AddMedicineAsync()
    {
        var id = await _host.AddMedicine(this);
        if (id is not { } added || IsDisposed) return;
        _flow.AddMedicine(added);
        RefreshMedicines();
    }

    private void RefreshMedicines()
    {
        var rows = _host.Medicines().ToDictionary(m => m.Id);
        _medicineList.BeginUpdate();
        _medicineList.Items.Clear();
        foreach (var id in _flow.AddedMedicines)
        {
            if (!rows.TryGetValue(id, out var row)) continue;
            _medicineList.Items.Add(new ListViewItem([row.Name, row.DaysRemainingDisplay]));
        }
        _medicineList.EndUpdate();
        _noMedicine.Visible = _medicineList.Items.Count == 0;
        _medicineList.Visible = !_noMedicine.Visible;
    }

    private void RefreshSummary()
    {
        var channels = _loc.Get("Ui.GuidedSetup.Channel." + ChannelKey(_flow.Channels));
        var days = _flow.LeadDays;
        var names = _host.Medicines()
            .Where(m => _flow.AddedMedicines.Contains(m.Id))
            .Select(m => m.Name)
            .ToList();
        var lines = new List<string> { _loc.Get("Ui.GuidedSetup.Summary.Intro") };
        if (names.Count == 0)
        {
            lines.Add(days == 0
                ? _loc.Get("Ui.GuidedSetup.Summary.NoMedicineAtZero", channels)
                : _loc.Get("Ui.GuidedSetup.Summary.NoMedicine", days, channels));
        }
        else
        {
            lines.AddRange(names.Select(name => days == 0
                ? _loc.Get("Ui.GuidedSetup.Summary.MedicineAtZero", name, channels)
                : _loc.Get("Ui.GuidedSetup.Summary.Medicine", days, name, channels)));
            lines.Add(_loc.Get("Ui.GuidedSetup.Summary.Seed"));
        }
        if ((_flow.Channels & NotificationChannels.Email) != 0 && !_flow.SendsEmail)
        {
            lines.Add(_loc.Get("Ui.GuidedSetup.Email.SentByMaster"));
        }
        _summary.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);

        var warnsNoOne = _flow.WarnsNoOne(_savedEmail.HasRecipient);
        _noOneWarned.Visible = warnsNoOne;
        _addWindows.Visible = warnsNoOne;
    }

    private static string ChannelKey(NotificationChannels channels) => channels switch
    {
        NotificationChannels.Email => "Email",
        NotificationChannels.Both => "Both",
        _ => "Windows",
    };

    // "Also warn on Windows": the channel is added to the medicines of
    // this setup and to the start of new medicines, and the summary is
    // written again.
    private async Task AddWindowsChannelAsync()
    {
        _flow.AddWindowsChannel();
        (_flow.Channels == NotificationChannels.Both ? _channelBoth : _channelWindows).Checked = true;
        await SaveWarningAsync();
        if (!IsDisposed) RefreshSummary();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy > 0) return;
        _busy++;
        _next.Enabled = _back.Enabled = _notNow.Enabled = false;
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
                UseWaitCursor = false;
                _next.Enabled = _notNow.Enabled = true;
                _back.Enabled = _flow.CanGoBack;
            }
        }
    }

    private bool Failed(Exception ex)
    {
        _log.LogError(ex, "Guided setup: saving the {Step} step failed.", _flow.Current);
        if (!IsDisposed)
        {
            UiMessageBox.Show(this, ex.Message, _loc.Get("Ui.GuidedSetup.Error.Save"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        return false;
    }

    // ------------------ Layout helpers ------------------
    private void AddPage(GuidedSetupStep step, params Control[] rows)
    {
        var page = Group(rows);
        page.Dock = DockStyle.Top;
        page.Padding = new Padding(UiTheme.Space.L, UiTheme.Space.S, UiTheme.Space.L, UiTheme.Space.M);
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };
        scroller.Controls.Add(page);
        _pages[step] = scroller;
        _pageHost.Controls.Add(scroller);
    }

    private static Label Paragraph(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(TextWidth, 0),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(TextWidth, 0),
        ForeColor = UiColors.Hint,
    };

    private static Control Indented(Control control)
    {
        control.Margin = new Padding(UiTheme.Space.L, 0, 0, UiTheme.Space.XS);
        return control;
    }

    // Controls stacked in a panel of their own (a radio button group).
    private static FlowLayoutPanel Group(params Control[] rows)
    {
        var group = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        foreach (var row in rows)
        {
            // Indented rows and buttons keep the margin they were given.
            if (row.Margin == ControlDefaultMargin) row.Margin = new Padding(0, 0, 0, UiTheme.Space.S);
            group.Controls.Add(row);
        }
        return group;
    }

    // The margin WinForms gives every control.
    private static readonly Padding ControlDefaultMargin = new(3);

    private LinkLabel Link(string key, Func<Task> action)
    {
        var link = new LinkLabel { AutoSize = true, Text = _loc.Get(key), Margin = new Padding(UiTheme.Space.L, 0, 0, UiTheme.Space.XS) };
        link.LinkClicked += async (_, _) =>
        {
            if (_busy > 0) return;
            await action();
        };
        return link;
    }
}
