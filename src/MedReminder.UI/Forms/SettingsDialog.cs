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

// Settings split into sections, listed on the left:
//   Email  — SMTP host / port / TLS / user, DPAPI-protected
//            password, send test.
//   Startup — auto-start with Windows.
//   Backup — DB export / import.
//
// The editable SMTP fields are serialized to
// %LOCALAPPDATA%\MedReminder\smtp.settings.json (added to the
// IConfiguration chain in Program.cs with reloadOnChange=true, so
// IOptionsMonitor<SmtpSettings> refreshes without a restart).
internal sealed partial class SettingsDialog : MedReminderFormBase
{
    private readonly IOptionsMonitor<SmtpSettings> _smtpMonitor;
    private readonly IOptionsMonitor<NotificationSettings> _notificationMonitor;
    private readonly IOptionsMonitor<BackupSettings> _backupMonitor;
    private readonly IOptionsMonitor<UserSettings> _userMonitor;
    private readonly ISmtpCredentialStore _credentialStore;
    private readonly IEmailNotificationService _emailService;
    private readonly IAutoStartService _autoStart;
    private readonly IBackupService _backup;
    private readonly IBackupStateStore _backupState;
    private readonly IApplicationRestarter _restarter;
    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry _profileRegistry;
    private readonly ILocalizationService _loc;
    private readonly IReferenceCatalogueQueryService _catalogueQuery;
    // C.3: encrypted export / import (§5). The dialogs run the services
    // off the UI thread; the SettingsDialog only launches them.
    private readonly IExportService _exportService;
    private readonly IImportService _importService;

    // C.3+ (docs/analysis/ANALYSIS-C3PLUS-CLOUD-BACKUP.md §5): the
    // Cloud Backup section reads / writes the DPAPI-cached backup
    // passphrase, and the Restore-from-cloud dialog enumerates and
    // applies snapshots via the cloud-restore service.
    private readonly ICloudBackupPassphraseStore _cloudPassStore;
    private readonly ICloudRestoreService _cloudRestore;
    private readonly ICloudAccountService? _cloudAccounts;
    private readonly IArchiveStorage? _archiveStorage;
    private readonly MedReminder.UI.Hosting.SyncHostedService? _sync;
    private readonly IServiceScopeFactory? _scopes;

    // Email tab controls
    private TextBox _hostBox = null!;
    private NumericUpDown _portBox = null!;
    private CheckBox _useTlsBox = null!;
    private TextBox _usernameBox = null!;
    private TextBox _passwordBox = null!;
    private CheckBox _clearPasswordBox = null!;
    private TextBox _fromBox = null!;
    private TextBox _fromNameBox = null!;
    private TextBox _toBox = null!;
    // A3 (docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §5): the
    // optional per-profile secondary recipient, on the Notifications tab.
    private TextBox _caregiverBox = null!;
    // Prescription request (EVOLUTION-PROPOSALS §3.4): optional
    // per-profile doctor address, recipient of the explicit send from
    // PrescriptionRequestDialog only.
    private TextBox _doctorBox = null!;
    private NumericUpDown _timeoutBox = null!;
    private Label _passwordStatusLabel = null!;

    // My PIN section on the Notifications tab. Lets a non-admin
    // profile set or clear its own PIN without opening the
    // admin-only ProfilesManagerForm.
    private Label _pinStateLabel = null!;

    // Auto-start
    private CheckBox _autoStartCheck = null!;

    // Backup
    private Label _dbPathLabel = null!;
    private CheckBox _backupEnabledBox = null!;
    private TextBox _backupDirectoryBox = null!;
    private DateTimePicker _backupTimePicker = null!;
    private NumericUpDown _backupRetentionBox = null!;
    private Label _backupStatusLabel = null!;
    private Label _backupCloudWarningLabel = null!;

    // C.3+ Cloud Backup subsection controls.
    private CheckBox _cloudEnabledBox = null!;
    private TextBox _cloudDirectoryBox = null!;
    private NumericUpDown _cloudRetentionBox = null!;
    private Label _cloudPassStatusLabel = null!;
    private Button _cloudPassChangeButton = null!;
    // C.3++ Phase 2 (B.1 Phase 4a): folder or OneDrive target.
    private ComboBox? _cloudProviderBox;
    private Label? _cloudAccountLabel;
    private Button? _cloudSignInButton;
    private Button? _cloudBrowseButton;
    private Label? _cloudNotSyncWarning;
    // Phase 4b: the providers offered in the combo (null = folder), and the
    // account signed in for each in this dialog.
    private List<CloudProvider?> _cloudProviderChoices = [];
    private readonly Dictionary<CloudProvider, string> _cloudAccountIds = [];

    // Generale (Incremento 16b) — selezione lingua UI
    private ComboBox _languageCombo = null!;
    // Reference-catalogue country (M2). Dropdown populated with
    // countries actually present in the local catalogue plus a
    // synthetic "EU" entry for supranational authorisations.
    private ComboBox _referenceCountryCombo = null!;
    // Passive update check opt-in — surfaces new GitHub releases at
    // startup without downloading anything.
    private CheckBox _checkUpdatesBox = null!;
    // Per-profile text size (EVOLUTION-PROPOSALS.md §3.2), saved to
    // profiles\<id>\ui.settings.json with the rest of the General tab.
    private ComboBox _textSizeCombo = null!;
    private ComboBox _appearanceCombo = null!;

    // Space above each group of the General tab (label, field, help),
    // so a help text no longer touches the next label (baseline L3).
    private static readonly Padding GroupMargin = new(3, UiTheme.Space.L, 3, 3);

    // Shared component for the explanatory tooltips on the technical fields.
    // (spec Incremento 14: help in linea, tooltip diffusi). Un solo
    // ToolTip per dialog è la best practice WinForms.
    private readonly ToolTip _tooltips = new()
    {
        AutoPopDelay = 20_000,
        InitialDelay = 400,
        ReshowDelay = 200,
        ShowAlways = true,
    };

    // A3 (§5.2): validate the caregiver address exactly as the MailKit
    // adapter does. MimeKit accepts a bare local part as a valid mailbox
    // by default; a caregiver address must carry a domain, so parse with
    // AllowAddressesWithoutDomain off. Kept in sync with
    // MailKitEmailNotificationService.AddressParserOptions (the adapter's
    // copy), which is internal to the Infrastructure assembly.
    private static readonly MimeKit.ParserOptions _addressParserOptions = new()
    {
        AllowAddressesWithoutDomain = false,
    };

    public SettingsDialog(
        IOptionsMonitor<SmtpSettings> smtpMonitor,
        IOptionsMonitor<NotificationSettings> notificationMonitor,
        IOptionsMonitor<BackupSettings> backupMonitor,
        IOptionsMonitor<UserSettings> userMonitor,
        ISmtpCredentialStore credentialStore,
        IEmailNotificationService emailService,
        IAutoStartService autoStart,
        IBackupService backup,
        IBackupStateStore backupState,
        IApplicationRestarter restarter,
        ICurrentProfile currentProfile,
        IProfileRegistry profileRegistry,
        ILocalizationService localization,
        IReferenceCatalogueQueryService catalogueQuery,
        IExportService exportService,
        IImportService importService,
        ICloudBackupPassphraseStore cloudPassStore,
        ICloudRestoreService cloudRestore,
        MedReminder.UI.Hosting.SyncHostedService? sync = null,
        ICloudAccountService? cloudAccounts = null,
        IArchiveStorage? archiveStorage = null,
        IServiceScopeFactory? scopes = null)
    {
        _scopes = scopes;
        _sync = sync;
        _cloudAccounts = cloudAccounts;
        _archiveStorage = archiveStorage;
        _smtpMonitor = smtpMonitor;
        _notificationMonitor = notificationMonitor;
        _backupMonitor = backupMonitor;
        _userMonitor = userMonitor;
        _credentialStore = credentialStore;
        _emailService = emailService;
        _autoStart = autoStart;
        _backup = backup;
        _backupState = backupState;
        _restarter = restarter;
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;
        _loc = localization;
        _catalogueQuery = catalogueQuery;
        _exportService = exportService;
        _importService = importService;
        _cloudPassStore = cloudPassStore;
        _cloudRestore = cloudRestore;

        Text = _loc.Get("Ui.SettingsDialog.Title");
        // Resizable with a section list on the left and one section at a
        // time on the right (docs/analysis/ANALYSIS-UI-MODERNIZATION.md
        // §5.2, F6); a section taller than the window scrolls.
        Width = 900;
        Height = 640;
        MinimumSize = new System.Drawing.Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;

        _sectionHeading = new Label
        {
            AutoSize = true,
            Font = UiTheme.Fonts.Heading(),
            Margin = new Padding(0, 0, 0, UiTheme.Space.S),
            Padding = new Padding(UiTheme.Space.S, 0, 0, 0),
        };
        _sectionHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _sectionList = new NavigationPane(expandedWidth: 280);

        AddSection("Ui.SettingsDialog.Tab.General", Mdl2Glyph.Glyphs.Settings, BuildGeneralTab());
        // Increment 15d (docs/ANALYSIS-MULTI-USER.md §7.4): SMTP and
        // Backup sections are admin-only. Every profile still needs to
        // choose its own recipient — that lives in the Notifications
        // section, visible to admins and users alike.
        if (_currentProfile.IsAdmin)
        {
            AddSection("Ui.SettingsDialog.Tab.Email", Mdl2Glyph.Glyphs.Mail, BuildEmailTab());
        }
        AddSection("Ui.SettingsDialog.Tab.Notifications", Mdl2Glyph.Glyphs.Ringer, BuildNotificationsTab());
        // The Windows Run entry is a per-Windows-account setting, so
        // it must not be toggled by a non-admin profile: doing so
        // would change the auto-start behaviour for every profile of
        // the same Windows user. Admin-only, coherent with Email
        // gating (§7.4).
        if (_currentProfile.IsAdmin)
        {
            AddSection("Ui.SettingsDialog.Tab.Startup", Mdl2Glyph.Glyphs.Power, BuildStartupTab());
        }
        // Backup section: all profiles. Automatic-backup settings and
        // the Save/Run-now buttons are hidden for non-admin profiles;
        // the manual export/import buttons are always visible.
        AddSection("Ui.SettingsDialog.Tab.Backup", Mdl2Glyph.Glyphs.Save, BuildBackupTab());
        SelectSection(0);

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.Space.M, UiTheme.Space.M, UiTheme.Space.M, 0),
            Margin = Padding.Empty,
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(_sectionHeading, 0, 0);
        page.Controls.Add(_sectionHost, 0, 1);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Without a style the single row sizes to its tallest child, so a
        // long section grew past the window and its scroll bar never
        // showed; Percent keeps the row as tall as the table.
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _sectionList.Dock = DockStyle.Left;
        body.Controls.Add(_sectionList, 0, 0);
        body.Controls.Add(page, 1, 0);

        var closeButton = new Button
        {
            Text = _loc.Get("Common.Close"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            // GrowAndShrink: with GrowOnly the size reached with the
            // larger font was then scaled again by the display factor.
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new System.Drawing.Size(88, 32),
            Padding = new Padding(UiTheme.Space.M, 0, UiTheme.Space.M, 0),
        };
        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(UiTheme.Space.M, UiTheme.Space.S, UiTheme.Space.M, UiTheme.Space.S),
        };
        buttonPanel.Controls.Add(closeButton);

        Controls.Add(body);
        Controls.Add(buttonPanel);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    // Section list (§5.2): every section is built up front, as the tabs
    // were, so the text size and theme reach all of them on load; only
    // the selected one is visible.
    private readonly List<(NavigationItem Item, string Title, Control Body)> _sections = [];
    private readonly NavigationPane _sectionList;
    private readonly Panel _sectionHost;
    private readonly Label _sectionHeading;
    private int _selectedSection = -1;

    private void AddSection(string titleKey, string glyph, Control body)
    {
        var title = _loc.Get(titleKey);
        var index = _sections.Count;
        var item = _sectionList.AddItem(title, glyph, opensWindow: false, () => SelectSection(index));
        body.Dock = DockStyle.Fill;
        body.Visible = false;
        // Every section scrolls as a whole, with its content docked at
        // the top and sized to what it needs, as Backup does. A panel
        // that filled the section and scrolled by itself showed no scroll
        // bar after scaling when the content was only a little taller
        // than the window (Settings -> General with Large text).
        foreach (var content in body.Controls.OfType<ScrollableControl>().Where(c => c.Dock == DockStyle.Fill).ToList())
        {
            content.AutoScroll = false;
            content.AutoSize = true;
            if (content is FlowLayoutPanel flow) flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            else if (content is Panel panel) panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            content.Dock = DockStyle.Top;
        }
        if (body is ScrollableControl scrollable) scrollable.AutoScroll = true;
        _sectionHost.Controls.Add(body);
        _sections.Add((item, title, body));
    }

    private void SelectSection(int index)
    {
        if (index == _selectedSection || index < 0 || index >= _sections.Count) return;
        _selectedSection = index;
        _sectionHost.SuspendLayout();
        for (var i = 0; i < _sections.Count; i++)
        {
            var (item, title, body) = _sections[i];
            item.Selected = i == index;
            body.Visible = i == index;
            if (i == index)
            {
                _sectionHeading.Text = title;
                AccessibleDescription = title;
            }
        }
        _sectionHost.ResumeLayout(performLayout: true);
        // A section laid out while hidden can keep a stale scroll range.
        RelayoutTree(_sections[index].Body);
    }

    // Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / Ctrl+PageUp move
    // between sections, as they moved between the tabs.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var step = keyData switch
        {
            Keys.Control | Keys.Tab or Keys.Control | Keys.PageDown => 1,
            Keys.Control | Keys.Shift | Keys.Tab or Keys.Control | Keys.PageUp => -1,
            _ => 0,
        };
        if (step != 0 && _sections.Count > 0)
        {
            SelectSection((_selectedSection + step + _sections.Count) % _sections.Count);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------ Layout helpers ------------------
    private static TableLayoutPanel BuildFormTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    // A folder field and its Browse button: the field takes the width
    // the section leaves, so the button stays visible in a narrow
    // window or with Large text (a fixed 460 px field pushed it out).
    private static TableLayoutPanel BuildPathRow(TextBox path, Button browse)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browse.Anchor = AnchorStyles.Left;
        row.Controls.Add(path, 0, 0);
        row.Controls.Add(browse, 1, 0);
        return row;
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        // Top-aligned: a label that wraps to several lines, or a field
        // taller than one line, would otherwise centre the label below
        // the field's first line (ANALYSIS-UI-MODERNIZATION L6).
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(UiTheme.Space.XS, UiTheme.Space.S, UiTheme.Space.M, UiTheme.Space.XS) };
        // A table clamps a child to its cell, and a fixed-width field is
        // never grown back: after one layout pass with a narrow column
        // (the section is measured before it is docked) the time picker
        // and number boxes (docked left in Email) stayed a few pixels
        // wide. The minimum keeps them.
        if (input is CheckBox check)
        {
            WrapCaption(check);
        }
        else if (!input.AutoSize && (input.Dock is DockStyle.None or DockStyle.Left) && (input.Anchor & AnchorStyles.Right) == 0)
        {
            input.MinimumSize = new System.Drawing.Size(input.Width, input.MinimumSize.Height);
        }
        table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(lbl, 0, table.RowCount - 1);
        table.Controls.Add(input, 1, table.RowCount - 1);
    }

    // A check box does not wrap its caption while it sizes itself, so at
    // the minimum window size with Large text the caption was cut. It
    // takes the width of its cell instead and grows in height to the
    // wrapped caption.
    private static void WrapCaption(CheckBox check)
    {
        check.AutoSize = false;
        check.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        check.CheckAlign = System.Drawing.ContentAlignment.TopLeft;
        check.TextAlign = System.Drawing.ContentAlignment.TopLeft;
        void Fit()
        {
            var glyph = check.Font.Height + check.Padding.Horizontal + UiTheme.Space.XS;
            var text = TextRenderer.MeasureText(check.Text, check.Font,
                new System.Drawing.Size(Math.Max(1, check.Width - glyph), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            var height = Math.Max(text.Height, check.Font.Height) + UiTheme.Space.XS;
            if (check.Height != height) check.Height = height;
        }
        check.SizeChanged += (_, _) => Fit();
        check.FontChanged += (_, _) => Fit();
        check.TextChanged += (_, _) => Fit();
    }
}
