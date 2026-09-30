using System.ComponentModel;
using System.Reflection;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Donations;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Timeline;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.UpdateChecking;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Infrastructure.Email;
using MedReminder.Application.Overview;
using MedReminder.UI.Controls;
using MedReminder.UI.Tray;
using MedReminder.UI.UiExtensions;
using MedReminder.UI.Hosting;
using MedReminder.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Main window: medicine list, toolbar, tray integration.
// Async operations create a dedicated DI scope via
// IServiceScopeFactory — the DbContext is Scoped, must not be shared
// between threads or concurrent operations.
internal sealed class MainForm : MedReminderFormBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApplicationTrayIcon _tray;
    private readonly ILogger<MainForm> _log;
    private readonly ILocalizationService _loc;
    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry _profileRegistry;
    private readonly IApplicationRestarter _restarter;
    private readonly DonationService _donations;

    private DataGridView _grid = null!;
    private BindingList<MedicineListItem> _rows = [];
    // Every row of the last load; _rows is the part shown, without the
    // deactivated medicines unless _showInactive (session only).
    private List<MedicineListItem> _allRows = [];
    private bool _showInactive;
    // Summary card filter and search text (ANALYSIS-UI-MODERNIZATION §5.1).
    private MedicineListBucket _bucket = MedicineListBucket.All;
    private ToolStripTextBox _searchBox = null!;
    private NavigationPane _nav = null!;
    private readonly List<SummaryCard> _cards = [];
    private ToolStripMenuItem _showInactiveItem = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _lastCheckLabel = null!;
    private Panel _errorBanner = null!;
    private Label _errorBannerLabel = null!;

    // Index of the "Status" column so we can apply the badge
    // colors in RowPrePaint without looking the column up by name
    // every time.
    private int _statusColumnIndex = -1;
    // Bold font cached for the Status cells: creating a new Font
    // on every prepaint would be wasteful.
    private Font? _statusCellFont;

    private readonly bool _closeToTray = true;
    private bool _reallyExit;

    // Sorting state for the "Days remaining" column. The DataGridView
    private SortOrder _sortOrderDaysRemaining = SortOrder.None;
    private string? _sortColumn;
    // Sorting state for the "Name" column. The DataGridView does not
    private SortOrder _sortOrderName = SortOrder.None;

    public MainForm(
        IServiceScopeFactory scopeFactory,
        ApplicationTrayIcon tray,
        ILogger<MainForm> log,
        ILocalizationService localization,
        ICurrentProfile currentProfile,
        IProfileRegistry profileRegistry,
        IApplicationRestarter restarter,
        DonationService donations)
    {
        _scopeFactory = scopeFactory;
        _tray = tray;
        _log = log;
        _loc = localization;
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;
        _restarter = restarter;
        _donations = donations;

        // Title bar shows the active profile so multi-profile users
        // can always see which one is open (§12.1).
        Text = _loc.Get("Ui.MainForm.Title.WithProfile",
            _loc.Get("Ui.MainForm.Title"), _currentProfile.DisplayName);
        Width = 960;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 420);

        BuildLayout();
        WireTrayHandlers();

        Load += async (_, _) => await ReloadAsync();
        Load += (_, _) => WireSyncRefresh();
        Load += (_, _) => WireHandoverPrompt();
        Load += (_, _) => TryStartPassiveUpdateCheck();
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var menuStrip = BuildMenuStrip();
        var toolStrip = BuildToolStrip();
        var statusStrip = BuildStatusStrip();
        _grid = BuildGrid();
        _errorBanner = BuildErrorBanner();
        _nav = BuildNavigationPane();

        // Page: summary cards above the grid (§5.1).
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UiTheme.Space.L, UiTheme.Space.M, UiTheme.Space.L, UiTheme.Space.M),
            Margin = Padding.Empty,
        };
        // A column without a style sizes to its widest child, and the
        // grid's preferred width is the sum of its columns: at 150 % the
        // page then grew past the window. Percent keeps it in bounds.
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(BuildSummaryCards(), 0, 0);
        page.Controls.Add(_grid, 0, 1);

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
        body.Controls.Add(_nav, 0, 0);
        body.Controls.Add(page, 1, 0);
        _nav.Dock = DockStyle.Left;

        // TableLayout with 5 rows: menu, toolbar, error banner, body
        // (navigation + page), status. The MenuStrip must be added to
        // Controls AND assigned to MainMenuStrip so keyboard shortcuts
        // (Ctrl+N, F5, Alt+F4) work everywhere.
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // menu
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // toolbar
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // banner (Visible=false)
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// body
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
        container.Controls.Add(menuStrip, 0, 0);
        container.Controls.Add(toolStrip, 0, 1);
        container.Controls.Add(_errorBanner, 0, 2);
        container.Controls.Add(body, 0, 3);
        container.Controls.Add(statusStrip, 0, 4);

        Controls.Add(container);
        MainMenuStrip = menuStrip;

        // Below this width the navigation pane shows icons only.
        Resize += (_, _) => _nav.Collapsed = ClientSize.Width < ScaledLength(NavCollapseWidth);
        Load += (_, _) => _nav.Collapsed = ClientSize.Width < ScaledLength(NavCollapseWidth);

        // Control.Scale also scales the text box a tool strip hosts, so
        // a width scaled at build time was scaled twice; set it once
        // the layout has been scaled.
        Load += (_, _) => _searchBox.Width = ScaledLength(SearchBoxWidth);
    }

    private const int SearchBoxWidth = 240;

    private const int NavCollapseWidth = 900;

    // D3: the medicine list is the page of this window; the other entries
    // open the windows the menus already open.
    private NavigationPane BuildNavigationPane()
    {
        var nav = new NavigationPane();
        var medicines = nav.AddItem(_loc.Get("Ui.MainForm.Nav.Medicines"), Mdl2Glyph.Glyphs.BulletedList,
            opensWindow: false, () => _grid.Focus());
        medicines.Selected = true;
        nav.AddItem(MenuCaption("Ui.MainForm.Menu.Therapy.Timeline"), Mdl2Glyph.Glyphs.Calendar,
            opensWindow: true, ShowTherapyTimeline);
        nav.AddItem(MenuCaption("Ui.MainForm.Menu.Therapy.Report"), Mdl2Glyph.Glyphs.Document,
            opensWindow: true, async () => await ShowTherapyReportAsync());
        nav.AddItem(MenuCaption("Ui.MainForm.Menu.Therapy.RequestPrescription"), Mdl2Glyph.Glyphs.Mail,
            opensWindow: true, async () => await ShowPrescriptionRequestAsync());
        nav.AddSeparator();
        if (_currentProfile.IsAdmin)
        {
            nav.AddItem(MenuCaption("Ui.MainForm.Menu.Tools.Household"), Mdl2Glyph.Glyphs.Home,
                opensWindow: true, ShowHousehold);
        }
        nav.AddItem(MenuCaption("Ui.MainForm.Menu.Tools.Settings"), Mdl2Glyph.Glyphs.Settings,
            opensWindow: true, ShowSettings);
        return nav;
    }

    // Menu captions carry an access key (&) and an ellipsis; the
    // navigation pane and the toolbar show the plain caption, so the
    // same translation serves both.
    private string MenuCaption(string key)
        => _loc.Get(key).Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&")
            .TrimEnd('…', '.').Trim();

    private TableLayoutPanel BuildSummaryCards()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, UiTheme.Space.M),
        };
        foreach (var (bucket, key) in new[]
        {
            (MedicineListBucket.Empty, "Ui.MainForm.Summary.Empty"),
            (MedicineListBucket.Warning, "Ui.MainForm.Summary.Warning"),
            (MedicineListBucket.Suspended, "Ui.MainForm.Summary.Suspended"),
            (MedicineListBucket.All, "Ui.MainForm.Summary.All"),
        })
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var card = new SummaryCard(bucket, _loc.Get(key))
            {
                Dock = DockStyle.Fill,
                Pressed = bucket == _bucket,
            };
            card.Click += (_, _) => SelectBucket(card.Bucket);
            _cards.Add(card);
            row.Controls.Add(card);
        }
        // The last card needs no gap on its right.
        _cards[^1].Margin = Padding.Empty;
        return row;
    }

    // A second click on the active card clears the filter.
    private void SelectBucket(MedicineListBucket bucket)
    {
        _bucket = bucket == _bucket ? MedicineListBucket.All : bucket;
        foreach (var card in _cards) card.Pressed = card.Bucket == _bucket;
        ApplyFilters();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl+F moves to the search box, Esc there clears it.
        if (keyData == (Keys.Control | Keys.F))
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            return true;
        }
        if (keyData == Keys.Escape && _searchBox.Focused && _searchBox.Text.Length > 0)
        {
            _searchBox.Clear();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------ Menu bar ------------------
    private MenuStrip BuildMenuStrip()
    {
        // File
        // Note: Alt+F4 is NOT registered as ShortcutKeys — the OS
        // already handles it (closes / hides the window) and the
        // tray-hide in OnFormClosing is the intended behavior there.
        // "Exit" (Ctrl+Q) instead forces a real exit via _reallyExit.
        var fileMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.File"));
        // "Change profile…" — visible to every profile (§12.2, decision
        // §14a I: one profile at a time). Opens the picker and, on
        // confirm, sets the hint and restarts the app so the new
        // profile is fully isolated.
        fileMenu.DropDownItems.Add(BuildMenuItem(
            _loc.Get("Ui.MainForm.Menu.File.ChangeProfile"),
            Mdl2Glyph.Glyphs.Contact, Keys.None,
            () => { ChangeProfile(); return Task.CompletedTask; }));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        var fileExit = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.File.Exit"), null,
            (_, _) => { _reallyExit = true; Close(); })
        { ShortcutKeys = Keys.Control | Keys.Q };
        fileMenu.DropDownItems.Add(fileExit);

        // Therapy
        var therapyMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy"));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.NewMedicine"),
            Mdl2Glyph.Glyphs.Add, Keys.Control | Keys.N,
            async () => await ShowNewMedicineAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Edit"),
            Mdl2Glyph.Glyphs.Edit, Keys.F2,
            async () => await ShowEditMedicineAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.ChangeSchedule"),
            Mdl2Glyph.Glyphs.Notebook, Keys.None,
            async () => await ShowChangeScheduleAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Deactivate"),
            Mdl2Glyph.Glyphs.Cancel, Keys.None,
            async () => await DeactivateSelectedAsync()));
        // No shortcut: a deletion is only ever started from the menu.
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Delete"),
            Mdl2Glyph.Glyphs.Delete, Keys.None,
            async () => await DeleteSelectedAsync()));
        var showInactive = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.ShowInactive"))
        {
            CheckOnClick = true,
            Checked = _showInactive,
        };
        showInactive.CheckedChanged += (_, _) =>
        {
            _showInactive = showInactive.Checked;
            ApplyFilters();
        };
        _showInactiveItem = showInactive;
        therapyMenu.DropDownItems.Add(showInactive);
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.RegisterIntake"),
            Mdl2Glyph.Glyphs.CheckMark, Keys.Control | Keys.I,
            async () => await ShowRegisterIntakeAsync()));
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Report"),
            Mdl2Glyph.Glyphs.Document, Keys.Control | Keys.P,
            async () => await ShowTherapyReportAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Timeline"),
            Mdl2Glyph.Glyphs.Calendar, Keys.Control | Keys.T,
            () => { ShowTherapyTimeline(); return Task.CompletedTask; }));
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.RequestPrescription"),
            Mdl2Glyph.Glyphs.Mail, Keys.None,
            async () => await ShowPrescriptionRequestAsync()));

        // Scorte
        var stockMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Stock"));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.AddPackage"),
            Mdl2Glyph.Glyphs.Package, Keys.Control | Keys.Shift | Keys.A,
            async () => await ShowStockDialogAsync(StockOperationKind.NewPackage)));
        // Independent of the selected row: the scan identifies the medicine.
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.RestockFromBarcode"),
            Mdl2Glyph.Glyphs.Package, Keys.None,
            async () => await RestockFromBarcodeAsync()));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Adjust"),
            Mdl2Glyph.Glyphs.Warning, Keys.None,
            async () => await ShowStockDialogAsync(StockOperationKind.NegativeCorrection)));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Count"),
            Mdl2Glyph.Glyphs.Search, Keys.None,
            async () => await ShowStockCountAsync()));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.History"),
            Mdl2Glyph.Glyphs.History, Keys.Control | Keys.H,
            async () => await ShowFactHistoryAsync()));
        stockMenu.DropDownItems.Add(new ToolStripSeparator());
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Refresh"),
            Mdl2Glyph.Glyphs.Refresh, Keys.F5,
            async () => await ReloadAsync()));

        // Strumenti
        var toolsMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Tools"));
        toolsMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Tools.CheckNow"),
            Mdl2Glyph.Glyphs.Sync, Keys.Control | Keys.R,
            async () => await RunMonitorAsync()));
        // Household step H3d (ANALYSIS-HOUSEHOLD-MASTER-DEVICE.md §4.3):
        // sync and the installation are administrators' tools, hidden from
        // other profiles like Manage profiles.
        if (_currentProfile.IsAdmin)
        {
            toolsMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Tools.Sync"),
                Mdl2Glyph.Glyphs.Sync, Keys.None,
                () => { ShowSync(); return Task.CompletedTask; }));
            toolsMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Tools.Household"),
                Mdl2Glyph.Glyphs.Sync, Keys.None,
                () => { ShowHousehold(); return Task.CompletedTask; }));
        }
        toolsMenu.DropDownItems.Add(new ToolStripSeparator());
        // Manage profiles… — admin only. The design (§12.2) is
        // clear: non-admin users must not see this entry at all,
        // not merely see it disabled. The check is repeated inside
        // the form as defense-in-depth.
        if (_currentProfile.IsAdmin)
        {
            toolsMenu.DropDownItems.Add(BuildMenuItem(
                _loc.Get("Ui.MainForm.Menu.Tools.ManageProfiles"),
                Mdl2Glyph.Glyphs.Contact, Keys.None,
                () => { ShowProfilesManager(); return Task.CompletedTask; }));
        }
        toolsMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Tools.Settings"),
            Mdl2Glyph.Glyphs.Settings, Keys.Control | Keys.Oemcomma,
            () => { ShowSettings(); return Task.CompletedTask; }));

        // Aiuto (?)
        var helpMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Help"));
        var helpGuide = BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Help.Guide"),
            Mdl2Glyph.Glyphs.Help, Keys.F1,
            () => { OpenUserGuide(); return Task.CompletedTask; });
        var helpCheckUpdates = BuildMenuItem(
            _loc.Get("Ui.MainForm.Menu.Help.CheckUpdates"),
            Mdl2Glyph.Glyphs.Sync, Keys.None,
            async () => await ManualUpdateCheckAsync());
        var helpAbout = BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Help.About"),
            Mdl2Glyph.Glyphs.Info, Keys.None,
            () => { ShowAboutDialog(); return Task.CompletedTask; });
        helpMenu.DropDownItems.Add(helpGuide);
        helpMenu.DropDownItems.Add(new ToolStripSeparator());
        helpMenu.DropDownItems.Add(helpCheckUpdates);
        helpMenu.DropDownItems.Add(helpAbout);

        // Support Development (A6). Only shown when the donation feature
        // is enabled and a provider is configured — no nagware, no
        // auto-popup. Hidden entirely otherwise (§8.1).
        if (_donations.IsFeatureAvailable)
        {
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(BuildMenuItem(
                _loc.Get("Ui.MenuHelp.SupportDevelopment"),
                Mdl2Glyph.Glyphs.HealthReport, Keys.None,
                () => { ShowDonateDialog(); return Task.CompletedTask; }));
        }

        var strip = new MenuStrip { Dock = DockStyle.Top };
        strip.Items.Add(fileMenu);
        strip.Items.Add(therapyMenu);
        strip.Items.Add(stockMenu);
        strip.Items.Add(toolsMenu);
        strip.Items.Add(helpMenu);

        // Drop-downs draw item images at their ImageScalingSize; match
        // it to the scaled glyphs so icons grow with the text.
        var iconSize = new Size(ScaledIconSize(16), ScaledIconSize(16));
        foreach (ToolStripItem top in strip.Items)
        {
            if (top is ToolStripMenuItem menu)
            {
                menu.DropDown.ImageScalingSize = iconSize;
            }
        }
        return strip;
    }

    private ToolStripMenuItem BuildMenuItem(
        string text, string glyph, Keys shortcut, Func<Task> action)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = Mdl2Glyph.Create(glyph, size: ScaledIconSize(16)),
        };
        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }
        item.Click += async (_, _) => await action();
        return item;
    }

    private void OpenUserGuide()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var dialog = new HelpViewerForm(
                scope.ServiceProvider.GetRequiredService<ILocalizationService>());
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.GuideOpenError"), ex);
        }
    }

    private void ShowAboutDialog()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var dialog = scope.ServiceProvider.GetRequiredService<AboutDialog>();
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open the About dialog.");
            MessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDonateDialog()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var dialog = scope.ServiceProvider.GetRequiredService<DonateForm>();
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open the Support Development dialog.");
            MessageBox.Show(this, ex.Message,
                _loc.Get("Common.Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Manual "Check for updates now" entry — always runs regardless
    // of the opt-in flag and always reports the outcome (up to date,
    // new version, or error).
    private async Task ManualUpdateCheckAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var checker = scope.ServiceProvider.GetRequiredService<IUpdateChecker>();
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            UseWaitCursor = true;
            var result = await checker.CheckAsync(current, cts.Token);
            UseWaitCursor = false;

            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    MessageBox.Show(this,
                        _loc.Get("Ui.UpdateCheck.UpToDate"),
                        _loc.Get("Ui.UpdateCheck.Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;

                case UpdateCheckStatus.NewVersionAvailable:
                    UpdateCheckPrompt.Show(this, _loc, result);
                    break;

                case UpdateCheckStatus.Error:
                default:
                    MessageBox.Show(this,
                        _loc.Get("Ui.UpdateCheck.Error", result.ErrorMessage ?? string.Empty),
                        _loc.Get("Ui.UpdateCheck.Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            _log.LogWarning(ex, "Manual update check failed.");
            MessageBox.Show(this,
                _loc.Get("Ui.UpdateCheck.Error", ex.Message),
                _loc.Get("Ui.UpdateCheck.Title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Fire-and-forget passive check on window load. Respects
    // UserSettings.CheckForUpdatesOnStartup (default true). A new
    // version pops the shared prompt; every other outcome (up to
    // date, network error, rate limit) is silent — the startup
    // path must never bother the user with transient failures.
    // Whatever the outcome, StartupUpdateCheckSignal is marked so
    // the remote catalogue refresh can follow (it waits for it).
    private void TryStartPassiveUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            StartupUpdateCheckSignal? signal = null;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                signal = scope.ServiceProvider.GetService<StartupUpdateCheckSignal>();
                var settings = scope.ServiceProvider
                    .GetRequiredService<IOptionsMonitor<UserSettings>>()
                    .CurrentValue;
                if (!settings.CheckForUpdatesOnStartup) return;

                var checker = scope.ServiceProvider.GetRequiredService<IUpdateChecker>();
                var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var result = await checker.CheckAsync(current, cts.Token);

                if (result.Status != UpdateCheckStatus.NewVersionAvailable) return;

                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    UpdateCheckPrompt.Show(this, _loc, result);
                }));
            }
            catch (Exception ex)
            {
                _log.LogInformation(ex, "Startup update check failed silently.");
            }
            finally
            {
                signal?.MarkCompleted();
            }
        });
    }

    // Red banner that appears above the grid when ReloadAsync
    // fails. Includes a "Retry" button because the status label at
    // the bottom of the page is too subtle for an error that blocks
    // the main functionality (spec §19: non-fatal error, but the UI
    // must still surface it).
    private Panel BuildErrorBanner()
    {
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = UiColors.HighContrast ? SystemColors.Info : UiTheme.Palette.DangerBack,
            Padding = new Padding(12, 8, 12, 8),
            Visible = false,
        };

        _errorBannerLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            ForeColor = UiColors.HighContrast ? SystemColors.InfoText : UiTheme.Palette.DangerText,
            Font = new Font(Font, FontStyle.Bold),
            Text = _loc.Get("Ui.MainForm.ErrorBanner.Load"),
        };

        var retryButton = new Button
        {
            Text = _loc.Get("Ui.MainForm.ErrorBanner.Retry"),
            Dock = DockStyle.Right,
            AutoSize = true,
        };
        retryButton.Click += async (_, _) => await ReloadAsync();

        banner.Controls.Add(retryButton);
        banner.Controls.Add(_errorBannerLabel);
        return banner;
    }

    private void ShowErrorBanner(string message)
    {
        _errorBannerLabel.Text = message;
        _errorBanner.Visible = true;
    }

    private void HideErrorBanner()
    {
        _errorBanner.Visible = false;
    }

    // Toolbar (D4, F5): the two most frequent actions and the search
    // box. Every other command stays in the menus with its shortcut, in
    // the navigation pane and in the grid's context menu.
    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(UiTheme.Space.S, UiTheme.Space.XS, UiTheme.Space.S, UiTheme.Space.XS),
            ImageScalingSize = new Size(ScaledIconSize(20), ScaledIconSize(20)),
            AutoSize = true,
        };
        strip.Items.Add(BuildToolbarButton(MenuCaption("Ui.MainForm.Menu.Therapy.NewMedicine"),
            Mdl2Glyph.Glyphs.Add,
            async () => await ShowNewMedicineAsync()));
        strip.Items.Add(BuildToolbarButton(MenuCaption("Ui.MainForm.Menu.Therapy.RegisterIntake"),
            Mdl2Glyph.Glyphs.CheckMark,
            async () => await ShowRegisterIntakeAsync()));

        _searchBox = new ToolStripTextBox
        {
            Alignment = ToolStripItemAlignment.Right,
            AutoSize = false,
            Width = SearchBoxWidth,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, UiTheme.Space.XS, UiTheme.Space.S, UiTheme.Space.XS),
            AccessibleName = _loc.Get("Ui.MainForm.Search.Placeholder"),
        };
        _searchBox.TextBox.PlaceholderText = _loc.Get("Ui.MainForm.Search.Placeholder");
        _searchBox.TextChanged += (_, _) => ApplyFilters();
        strip.Items.Add(_searchBox);
        return strip;
    }

    private ToolStripButton BuildToolbarButton(
        string text, string glyph, Func<Task> action)
    {
        var b = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Image = Mdl2Glyph.Create(glyph, size: ScaledIconSize(20)),
            ImageScaling = ToolStripItemImageScaling.None,
            AutoSize = true,
            Padding = new Padding(UiTheme.Space.S, UiTheme.Space.XS, UiTheme.Space.S, UiTheme.Space.XS),
        };
        b.Click += async (_, _) => await action();
        return b;
    }

    private async Task ShowTherapyReportAsync()
    {
        try
        {
            List<MedReminder.Application.Reporting.TherapyReportEntry> entries;
            DateOnly today;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var medicineRepo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
                var slotRepo = scope.ServiceProvider.GetRequiredService<IMedicationAdministrationSlotRepository>();
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

                var medicines = await medicineRepo.ListAllAsync(CancellationToken.None);
                entries = new List<MedReminder.Application.Reporting.TherapyReportEntry>(medicines.Count);
                foreach (var m in medicines)
                {
                    var slots = await slotRepo.ListForMedicineAsync(m.Id, CancellationToken.None);
                    entries.Add(new MedReminder.Application.Reporting.TherapyReportEntry(m, slots));
                }

                today = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);
            }

            var profileName = _currentProfile.DisplayName;
            using var dialog = new TherapyReportDialog(
                options => MedReminder.Application.Reporting.TherapyCardBuilder.Build(
                    entries, today, options, profileName, _loc.CurrentCulture, _loc),
                _loc);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.GenerateReport"), ex);
        }
    }

    // Read-only timeline view (EVOLUTION-PROPOSALS §4.3). Each load
    // runs in its own DI scope; "Show in list" selects the medicine in
    // the grid so the existing actions apply to it.
    private void ShowTherapyTimeline()
    {
        try
        {
            using var dialog = new TherapyTimelineForm(_loc, LoadTherapyTimelineAsync, GetSelectedRow()?.Id);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedMedicineId is { } id)
            {
                SelectGridRow(id);
            }
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.OpenTimeline"), ex);
        }
    }

    private async Task<TherapyTimeline> LoadTherapyTimelineAsync(TimelineWindow? window)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<TherapyTimelineQuery>();
        return await query.LoadAsync(window, CancellationToken.None);
    }

    private void SelectGridRow(Guid medicineId)
    {
        if (!_showInactive && _allRows.Any(r => r.Id == medicineId && !r.IsActive))
        {
            // Raises CheckedChanged, which shows the inactive rows.
            _showInactiveItem.Checked = true;
        }
        // A card or search filter that hides the medicine is cleared.
        if (!_rows.Any(r => r.Id == medicineId))
        {
            _bucket = MedicineListBucket.All;
            foreach (var card in _cards) card.Pressed = card.Bucket == _bucket;
            if (_searchBox.Text.Length > 0) _searchBox.Clear(); // raises ApplyFilters
            else ApplyFilters();
        }
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not MedicineListItem item || item.Id != medicineId) continue;
            _grid.ClearSelection();
            var cell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible);
            if (cell is not null) _grid.CurrentCell = cell;
            row.Selected = true;
            _grid.Focus();
            return;
        }
    }
    // Prescription request draft for the selected medicine
    // (EVOLUTION-PROPOSALS §3.4). Available for any medicine, whatever
    // its stock status. Nothing is sent from here: the dialog owns the
    // explicit delivery actions.
    private async Task ShowPrescriptionRequestAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        MedReminder.Application.Notifications.EmailMessage draft;
        string doctorAddress;
        bool smtpConfigured;
        bool isMaster;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;

            draft = PrescriptionRequestTexts.Build(medicine, _currentProfile.DisplayName, _loc);
            doctorAddress = scope.ServiceProvider
                .GetRequiredService<IOptionsMonitor<NotificationSettings>>().CurrentValue.DoctorAddress;
            smtpConfigured = scope.ServiceProvider
                .GetRequiredService<IOptionsMonitor<SmtpSettings>>().CurrentValue.IsConfigured;
            // Household step H4c (C5): a device that is not the master
            // offers the mail client only.
            isMaster = await scope.ServiceProvider.GetRequiredService<IMasterRole>().SendsEmailAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new PrescriptionRequestDialog(
            draft, doctorAddress, smtpConfigured, SendPrescriptionRequestAsync, _loc, isMaster);
        dialog.ShowDialog(this);
    }

    private async Task SendPrescriptionRequestAsync(
        string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var usecase = scope.ServiceProvider.GetRequiredService<SendPrescriptionRequest>();
        await usecase.ExecuteAsync(recipient, subject, body, cancellationToken);
    }

    private StatusStrip BuildStatusStrip()
    {
        // Profile indicator on the left (docs/ANALYSIS-MULTI-USER.md
        // §12.1). Admin gets a distinct visual badge so the user
        // knows whose account is running.
        var profileLabel = new ToolStripStatusLabel(
            _loc.Get(_currentProfile.IsAdmin
                ? "Ui.MainForm.Status.ProfileAdmin"
                : "Ui.MainForm.Status.Profile",
                _currentProfile.DisplayName))
        {
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, _currentProfile.IsAdmin ? FontStyle.Bold : FontStyle.Regular),
        };
        // Only the admin badge gets its own colour; the other label
        // keeps the default so the strip renderer draws it in the
        // palette's text colour.
        if (_currentProfile.IsAdmin)
        {
            profileLabel.ForeColor = UiTheme.Palette.Accent;
        }
        _statusLabel = new ToolStripStatusLabel(_loc.Get("Ui.App.Ready"))
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _lastCheckLabel = new ToolStripStatusLabel(_loc.Get("Ui.MainForm.LastCheck.None"))
        {
            TextAlign = ContentAlignment.MiddleRight,
        };
        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.Add(profileLabel);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_statusLabel);
        strip.Items.Add(_lastCheckLabel);
        return strip;
    }

    // ------------------ Profile switch / manage ------------------

    private void ChangeProfile()
    {
        try
        {
            using var picker = new ProfilePickerForm(_profileRegistry, _loc);
            var result = picker.ShowDialog(this);
            if (result != DialogResult.OK || picker.SelectedProfile is null)
            {
                return;
            }
            var chosen = picker.SelectedProfile;
            if (string.Equals(chosen.Id, _currentProfile.Id, StringComparison.Ordinal))
            {
                // Same profile — nothing to do.
                return;
            }

            // Persist the new hint so the restarted process opens the
            // chosen profile without showing the picker again
            // (§6.1 flow). The hint alone is not enough — with more
            // than one profile ChooseProfile still shows the picker
            // unless "--profile <id>" is passed, so pass it too.
            _profileRegistry.SetActiveProfileHint(chosen.Id);
            _restarter.RestartAndExit(["--profile", chosen.Id]);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ChangeProfile"), ex);
        }
    }

    // B.1 Phase 3d: after a sync applied changes from another device,
    // the list shows them.
    private void WireSyncRefresh()
    {
        using var scope = _scopeFactory.CreateScope();
        var status = scope.ServiceProvider.GetRequiredService<SyncStatus>();
        EventHandler handler = (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Func<Task>(ReloadAsync));
        };
        status.RemoteChangesApplied += handler;
        FormClosed += (_, _) => status.RemoteChangesApplied -= handler;

        // Household step H5b: the profile's group was rotated after a device
        // removal; the new key is taken at the next start, before the
        // database is used.
        EventHandler rotated = (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated || !status.NeedsNewKey || _rotatedKeyAsked) return;
            BeginInvoke(new Func<Task>(OfferRestartForRotatedKeyAsync));
        };
        status.Changed += rotated;
        FormClosed += (_, _) => status.Changed -= rotated;
    }

    // Household step H4b (§7.2 step 3): when this device is elected master,
    // an administrator is asked, once per election and session, to run the
    // handover wizard. Later leaves it in Tools → Installation.
    private readonly HashSet<Guid> _handoverAsked = [];
    private bool _newKeyAsked;
    private HouseholdHostedService? _householdService;

    private void WireHandoverPrompt()
    {
        if (!_currentProfile.IsAdmin) return;
        using var scope = _scopeFactory.CreateScope();
        var household = scope.ServiceProvider.GetRequiredService<HouseholdHostedService>();
        _householdService = household;
        EventHandler handler = (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Func<Task>(PromptHandoverAsync));
        };
        household.Changed += handler;
        FormClosed += (_, _) => household.Changed -= handler;
    }

    private async Task PromptHandoverAsync()
    {
        try
        {
            MedReminder.Application.Household.HandoverView? view;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                view = await scope.ServiceProvider.GetRequiredService<MedReminder.Application.Household.MasterHandover>()
                    .PendingAsync(CancellationToken.None);
            }
            // Step H5a: a device was removed elsewhere; an administrator
            // enters the new key in the installation window.
            if (!_newKeyAsked && _householdService?.LastResult?.NewKeyRequired == true)
            {
                _newKeyAsked = true;
                MessageBox.Show(this, _loc.Get("Ui.HouseholdDialog.Status.NewKey"), _loc.Get("Ui.HouseholdDialog.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (view is null || !_handoverAsked.Add(view.Election.ElectionId)) return;
            if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.HandoverWizard.Prompt"), _loc.Get("Ui.HandoverWizard.Title"),
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            await HandoverWizardForm.ShowIfPendingAsync(this, _scopeFactory, _loc);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.Household"), ex);
        }
    }

    private bool _rotatedKeyAsked;

    private async Task OfferRestartForRotatedKeyAsync()
    {
        if (_rotatedKeyAsked) return;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetRequiredService<ISyncSettingsStore>().Load() is not { } settings) return;
            var key = await scope.ServiceProvider.GetRequiredService<MedReminder.Application.Household.HouseholdKeyring>()
                .NewerGrantAsync(_currentProfile.Id, settings.GroupId, settings.KeyVersion, CancellationToken.None);
            if (key is null) return;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key.Key);
            _rotatedKeyAsked = true;
            if (ConfirmDialog.Show(_loc, this, _loc.Get("Ui.MainForm.RotatedKey.Prompt"), _loc.Get("Ui.HouseholdDialog.Title"),
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _restarter.RestartAndExit(["--profile", _currentProfile.Id]);
            }
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.Household"), ex);
        }
    }

    private void ShowSync()
    {
        if (!_currentProfile.IsAdmin) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            using var dialog = new SyncDialog(
                _scopeFactory,
                sp.GetRequiredService<SyncHostedService>(),
                sp.GetRequiredService<SyncStatus>(),
                sp.GetRequiredService<ISyncSettingsStore>(),
                sp.GetRequiredService<ICloudAccountService>(),
                _currentProfile,
                _loc,
                _restarter);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.Sync"), ex);
        }
    }

    private void ShowHousehold()
    {
        if (!_currentProfile.IsAdmin) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            using var dialog = new HouseholdDialog(
                _scopeFactory,
                sp.GetRequiredService<HouseholdHostedService>(),
                sp.GetRequiredService<ICloudAccountService>(),
                _currentProfile,
                _loc,
                _restarter);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.Household"), ex);
        }
    }

    private void ShowProfilesManager()
    {
        if (!_currentProfile.IsAdmin)
        {
            // Defense in depth: the menu entry is hidden for non-admin
            // users but a slash command or accessibility tool could
            // still trigger this handler.
            return;
        }
        try
        {
            using var dialog = new ProfilesManagerForm(
                _profileRegistry, _currentProfile, _loc, _scopeFactory);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ManageProfiles"), ex);
        }
    }

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            AllowUserToResizeRows = false,
        };
        // 36 px rows at Normal size (§5.1); MedReminderFormBase scales
        // the row template with the display and the text size.
        grid.RowTemplate.Height = 36;
        // Every column fills by weight down to a minimum, so with Large
        // text at 150 % the list fits the page instead of scrolling
        // sideways; the name column takes the largest share.
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Medicine"),
            DataPropertyName = nameof(MedicineListItem.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 250,
            MinimumWidth = 160,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Stock"),
            DataPropertyName = nameof(MedicineListItem.StockDisplay),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 120,
            MinimumWidth = 96,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.DailyRate"),
            DataPropertyName = nameof(MedicineListItem.DailyRateDisplay),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100,
            MinimumWidth = 80,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.DaysRemaining"),
            DataPropertyName = nameof(MedicineListItem.DaysRemainingDisplay),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100,
            MinimumWidth = 80,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.RunOut"),
            DataPropertyName = nameof(MedicineListItem.EtaDisplay),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 110,
            MinimumWidth = 88,
        });
        _statusCellFont = new Font(Font, FontStyle.Bold);
        var statusColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Status"),
            DataPropertyName = nameof(MedicineListItem.StatusDisplay),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 110,
            MinimumWidth = 88,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                // Alignment and font are column-level: they do not
                // change per row, so we set them here once. Status
                // colors instead vary and must be applied in
                // RowPrePaint.
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                Font = _statusCellFont,
            },
        };
        grid.Columns.Add(statusColumn);
        _statusColumnIndex = statusColumn.Index;
        grid.DataSource = _rows;
        grid.CellFormatting += (s, e) =>
        {
            if (grid.Columns[e.ColumnIndex].DataPropertyName ==
                nameof(MedicineListItem.DaysRemaining))
            {
                e.Value ??= "—";
                e.FormattingApplied = true;
            }
        };
        grid.ColumnHeaderMouseClick += (s, e) =>
        {
            var column = grid.Columns[e.ColumnIndex];

            if (grid.DataSource == null) return;

            if (column.DataPropertyName == nameof(MedicineListItem.DaysRemainingDisplay))
            {
                // Toggle if the same column is clicked again, otherwise reset to ascending.
                if (_sortColumn == column.DataPropertyName)
                {
                    _sortOrderDaysRemaining = _sortOrderDaysRemaining == SortOrder.Ascending
                        ? SortOrder.Descending
                        : SortOrder.Ascending;
                }
                else
                {
                    _sortColumn = column.DataPropertyName;
                    _sortOrderDaysRemaining = SortOrder.Ascending;
                }

                var items = ((IEnumerable<MedicineListItem>)grid.DataSource!).ToList();

                items = _sortOrderDaysRemaining == SortOrder.Ascending
                    ? [.. items.OrderBy(x => x.DaysRemaining ?? int.MaxValue)]
                    : [.. items.OrderByDescending(x => x.DaysRemaining ?? int.MinValue)];

                grid.DataSource = new BindingList<MedicineListItem>(items);

                column.HeaderCell.SortGlyphDirection = _sortOrderDaysRemaining;

                grid.Columns.Cast<DataGridViewColumn>()
                    .FirstOrDefault(c => c.DataPropertyName == nameof(MedicineListItem.Name))
                    ?.HeaderCell.SortGlyphDirection = SortOrder.None;

                grid.Refresh();
            }
            else if (column.DataPropertyName == nameof(MedicineListItem.Name))
            {
                // Toggle if the same column is clicked again, otherwise reset to ascending.
                if (_sortColumn == column.DataPropertyName)
                {
                    _sortOrderName = _sortOrderName == SortOrder.Ascending
                        ? SortOrder.Descending
                        : SortOrder.Ascending;
                }
                else
                {
                    _sortColumn = column.DataPropertyName;
                    _sortOrderName = SortOrder.Ascending;
                }

                var items = ((IEnumerable<MedicineListItem>)grid.DataSource!).ToList();
                items = _sortOrderName == SortOrder.Ascending
                    ? [.. items.OrderBy(x => x.Name)]
                    : [.. items.OrderByDescending(x => x.Name)];

                column.HeaderCell.SortGlyphDirection = _sortOrderName;

                grid.DataSource = new BindingList<MedicineListItem>(items);

                grid.Columns.Cast<DataGridViewColumn>()
                    .FirstOrDefault(c => c.DataPropertyName == nameof(MedicineListItem.DaysRemainingDisplay))
                    ?.HeaderCell.SortGlyphDirection = SortOrder.None;

                grid.Refresh();
            }
        };
        grid.RowPrePaint += OnRowPrePaint;
        grid.CellPainting += OnStatusCellPainting;
        grid.CellDoubleClick += async (_, _) => await ShowEditMedicineAsync();

        // Right-click selects the row under the pointer before the
        // context menu opens, so the command acts on that medicine.
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            grid.ClearSelection();
            var row = grid.Rows[e.RowIndex];
            var cell = row.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible);
            if (cell is not null) grid.CurrentCell = cell;
            row.Selected = true;
        };
        grid.ContextMenuStrip = BuildGridContextMenu();
        return grid;
    }

    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not MedicineListItem item) return;

        // High-contrast theme: keep the theme's colours; the Status
        // column still states the condition in words.
        if (UiColors.HighContrast)
        {
            row.DefaultCellStyle.BackColor = Color.Empty;
            row.DefaultCellStyle.ForeColor = Color.Empty;
            if (_statusColumnIndex >= 0 && _statusColumnIndex < row.Cells.Count)
            {
                var style = row.Cells[_statusColumnIndex].Style;
                style.BackColor = Color.Empty;
                style.ForeColor = Color.Empty;
                style.SelectionBackColor = Color.Empty;
                style.SelectionForeColor = Color.Empty;
            }
            return;
        }

        // Status is told once, in the Status cell (ANALYSIS-UI-MODERNIZATION
        // §5.1, F3): no whole-row tint, which competed with the selection.
        // Suspended and inactive rows read in secondary text.
        var palette = UiTheme.Palette;
        row.DefaultCellStyle.BackColor = Color.Empty;
        row.DefaultCellStyle.ForeColor = item.Status is MedicineRowStatus.Suspended or MedicineRowStatus.Inactive
            ? palette.TextSecondary
            : Color.Empty;

    }

    // Status as a pill (§5.1): tinted rounded background and coloured
    // text on the row's own background, so the selection highlight and
    // the status read together. Under high contrast the stock cell is
    // drawn and the text alone tells the status.
    private void OnStatusCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _statusColumnIndex || UiColors.HighContrast) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not MedicineListItem item || e.Graphics is null) return;

        var palette = UiTheme.Palette;
        var (bg, fg) = item.Status switch
        {
            MedicineRowStatus.Ok        => (palette.OkBack,      palette.OkText),
            MedicineRowStatus.Warning   => (palette.WarningBack, palette.WarningText),
            MedicineRowStatus.Empty     => (palette.DangerBack,  palette.DangerText),
            MedicineRowStatus.Suspended
                or MedicineRowStatus.Inactive => (palette.NeutralBack, palette.NeutralText),
            _                           => (palette.Surface,     palette.Text),
        };

        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        e.PaintBackground(e.CellBounds, selected);

        var text = e.FormattedValue as string ?? item.StatusDisplay;
        var font = e.CellStyle?.Font ?? _grid.Font;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var textSize = TextRenderer.MeasureText(e.Graphics, text, font, e.CellBounds.Size, flags);
        var padX = font.Height * 2 / 3;
        var padY = font.Height / 5;
        var width = Math.Min(e.CellBounds.Width - 2 * padY, textSize.Width + 2 * padX);
        var height = Math.Min(e.CellBounds.Height - 2 * padY, textSize.Height + 2 * padY);
        var pill = new Rectangle(
            e.CellBounds.X + (e.CellBounds.Width - width) / 2,
            e.CellBounds.Y + (e.CellBounds.Height - height) / 2,
            width, height);

        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = SummaryCard.RoundedRect(pill, Math.Max(2, height / 6)))
        using (var brush = new SolidBrush(bg))
        {
            e.Graphics.FillPath(brush, path);
        }
        e.Graphics.SmoothingMode = smoothing;
        TextRenderer.DrawText(e.Graphics, text, font, pill, fg, flags);
        e.Handled = true;
    }

    // Context menu of the grid (D4): the per-medicine commands of the
    // Therapy and Stock menus, next to the row they act on.
    private ContextMenuStrip BuildGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Edit"),
            Mdl2Glyph.Glyphs.Edit, Keys.None, async () => await ShowEditMedicineAsync()));
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.RegisterIntake"),
            Mdl2Glyph.Glyphs.CheckMark, Keys.None, async () => await ShowRegisterIntakeAsync()));
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.ChangeSchedule"),
            Mdl2Glyph.Glyphs.Notebook, Keys.None, async () => await ShowChangeScheduleAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.AddPackage"),
            Mdl2Glyph.Glyphs.Package, Keys.None, async () => await ShowStockDialogAsync(StockOperationKind.NewPackage)));
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Adjust"),
            Mdl2Glyph.Glyphs.Edit, Keys.None, async () => await ShowStockDialogAsync(StockOperationKind.NegativeCorrection)));
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.History"),
            Mdl2Glyph.Glyphs.History, Keys.None, async () => await ShowFactHistoryAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Deactivate"),
            Mdl2Glyph.Glyphs.Cancel, Keys.None, async () => await DeactivateSelectedAsync()));
        menu.ImageScalingSize = new Size(ScaledIconSize(16), ScaledIconSize(16));
        // Nothing to act on without a row (right-click below the last one).
        menu.Opening += (_, e) => e.Cancel = GetSelectedRow() is null;
        return menu;
    }

    private void WireTrayHandlers()
    {
        _tray.NotifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        _tray.OpenItem.Click += (_, _) => RestoreFromTray();
        _tray.CheckNowItem.Click += async (_, _) => await RunMonitorAsync();
        _tray.SettingsItem.Click += (_, _) => { RestoreFromTray(); ShowSettings(); };
        _tray.ExitItem.Click += (_, _) => { _reallyExit = true; Close(); };
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit || e.CloseReason != CloseReason.UserClosing || !_closeToTray)
        {
            return;
        }
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    // ------------------ Data loading ------------------
    private async Task ReloadAsync()
    {
        SetStatus(_loc.Get("Ui.MainForm.Status.Loading"));
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var loader = scope.ServiceProvider.GetRequiredService<MedicineOverviewLoader>();
            var items = await loader.LoadAsync(CancellationToken.None);

            _allRows = items.ToList();
            ApplyFilters();
            HideErrorBanner();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore caricamento medicine");
            SetStatus(_loc.Get("Ui.MainForm.Status.LoadError"));
            ShowErrorBanner(_loc.Get("Ui.MainForm.LoadBanner.Failure", ex.Message));
        }
    }

    // Rebuilds the shown rows from the last load, in load order: the
    // inactive toggle, then the summary card and the search text
    // (MedicineListFilter). The cards count the rows before the card and
    // search filters, so each card shows what a click on it would list.
    private void ApplyFilters()
    {
        var visible = MedicineListFilter.Visible(_allRows, _showInactive);
        var summary = MedicineListFilter.Summarize(visible);
        foreach (var card in _cards)
        {
            card.Count = card.Bucket switch
            {
                MedicineListBucket.Empty => summary.Empty,
                MedicineListBucket.Warning => summary.Warning,
                MedicineListBucket.Suspended => summary.Suspended,
                _ => summary.All,
            };
        }

        _rows = new BindingList<MedicineListItem>(
            MedicineListFilter.Apply(visible, _bucket, _searchBox.Text));
        _grid.DataSource = _rows;

        var hidden = _allRows.Count - visible.Count;
        if (_rows.Count != visible.Count)
        {
            SetStatus(_loc.Get("Ui.MainForm.Status.Filtered", _rows.Count, visible.Count));
        }
        else
        {
            SetStatus(hidden > 0
                ? _loc.Get("Ui.MainForm.Status.MedicinesLoadedHidden", _rows.Count, hidden)
                : _loc.Get("Ui.MainForm.Status.MedicinesLoaded", _rows.Count));
        }
    }

    private MedicineListItem? GetSelectedRow()
    {
        if (_grid.SelectedRows.Count == 0) return null;
        return _grid.SelectedRows[0].DataBoundItem as MedicineListItem;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    // ------------------ Actions ------------------
    // Build a fresh CatalogueAutocompleteContext for a dialog opening.
    // Each keystroke inside the dialog will spin up its own DI scope
    // to answer the search, so the scope owning the DbContext never
    // outlives one query. Returns null when the feature flag is off,
    // which puts the two autocomplete boxes into plain-text mode.
    private CatalogueAutocompleteContext? BuildCatalogueContext()
    {
        using var scope = _scopeFactory.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CatalogueFeatureOptions>>();
        if (!options.CurrentValue.Enabled) return null;

        var userSettings = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<UserSettings>>();
        var raw = userSettings.CurrentValue.ReferenceCountry ?? "IT";
        var country = CountryCode.TryParse(raw, out var parsed) ? parsed : CountryCode.Parse("IT");

        return new CatalogueAutocompleteContext(
            SearchCommercialName: (prefix, ctry, ct) => SearchCatalogueAsync(prefix, ctry, ct, byName: true),
            SearchActiveIngredient: (prefix, ctry, ct) => SearchCatalogueAsync(prefix, ctry, ct, byName: false),
            LookupByNationalCode: LookupReferenceByNationalCodeAsync,
            Country: country);
    }

    // Dependencies of the "Scan barcode" button in MedicineEditDialog
    // (A2). All singletons, so resolving them from a short-lived scope
    // is safe. The dialog ignores it when the catalogue context is null.
    private BarcodeScanContext BuildBarcodeScanContext()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        return new BarcodeScanContext(
            sp.GetRequiredService<IBarcodeParser>(),
            sp.GetRequiredService<ICameraCaptureService>(),
            sp.GetRequiredService<BarcodeCaptureOptions>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<BarcodeScanDialog>());
    }

    private async Task<IReadOnlyList<MedReminder.Domain.Catalogue.ReferenceMedicine>> SearchCatalogueAsync(
        string prefix, CountryCode country, CancellationToken cancellationToken, bool byName)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var usecase = scope.ServiceProvider.GetRequiredService<SearchCatalogueUseCase>();
        return byName
            ? await usecase.SearchByCommercialNameAsync(prefix, country, cancellationToken)
            : await usecase.SearchByActiveIngredientAsync(prefix, country, cancellationToken);
    }

    // Exact-lookup path used by MedicineEditDialog to re-hydrate the
    // AIFA LINK_FI / LINK_RCP URLs in Edit mode. Runs in a fresh DI
    // scope so the DbContext behind the reference-catalogue query
    // service is disposed straight after the call.
    private async Task<MedReminder.Domain.Catalogue.ReferenceMedicine?> LookupReferenceByNationalCodeAsync(
        CountryCode country, string nationalCode, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<MedReminder.Application.Catalogue.IReferenceCatalogueQueryService>();
        return await query.GetByNationalCodeAsync(country, nationalCode, cancellationToken);
    }

    private async Task ShowNewMedicineAsync(ReferenceMedicine? initialReference = null)
    {
        using var dialog = new MedicineEditDialog(
            MedicineEditDialog.EditMode.Create, _loc,
            catalogueContext: BuildCatalogueContext(),
            barcodeContext: BuildBarcodeScanContext(),
            initialReference: initialReference);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<AddMedicine>();
            var id = await usecase.ExecuteAsync(dialog.Result.ToAddCommand(), CancellationToken.None);
            _log.LogInformation("Medicina creata: {MedicineId}", id);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.CreateMedicine"), ex);
        }
    }

    private async Task ShowEditMedicineAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        MedicineEditResult seed;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var slotRepo = scope.ServiceProvider.GetRequiredService<IMedicationAdministrationSlotRepository>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;

            var slots = await slotRepo.ListForMedicineAsync(row.Id, CancellationToken.None);
            IReadOnlyList<AdministrationSlotEntry> seedSlots = [.. slots.Select(s => new AdministrationSlotEntry(s.Time, s.Dose, s.TimingLabel))];

            // Reconstruct the therapy's current schedule from the most
            // recent history entry, so the edit dialog opens
            // pre-populated with an existing advanced regime instead of
            // resetting to Simple.
            var schedules = scope.ServiceProvider.GetRequiredService<IMedicationScheduleHistoryRepository>();
            var history = await schedules.ListForMedicineAsync(row.Id, CancellationToken.None);
            MedicationScheduleHistory? latest = null;
            foreach (var entry in history)
            {
                if (latest is null || entry.EffectiveFrom > latest.EffectiveFrom)
                {
                    latest = entry;
                }
            }
            var currentSchedule = latest is null
                ? null
                : ScheduleCodec.Deserialize(
                    latest.ScheduleKind, latest.SchedulePayload,
                    latest.DosePerAdministration, latest.AdministrationsPerDay);

            seed = new MedicineEditResult(
                medicine.Name, medicine.ActiveIngredient, medicine.Package, medicine.Unit,
                medicine.DosePerAdministration, medicine.AdministrationsPerDay,
                medicine.StartDate, medicine.EndDate, medicine.ThresholdDays,
                medicine.DoctorName, medicine.Notes,
                InitialQuantity: 0m, medicine.NotificationChannels, medicine.IsActive,
                RemindOnDose: medicine.RemindOnDose,
                Slots: seedSlots,
                NationalCode: medicine.NationalCode,
                AtcCode: medicine.AtcCode,
                LinkedReferenceMedicineId: medicine.LinkedReferenceMedicineId,
                InitialSchedule: currentSchedule);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new MedicineEditDialog(
            MedicineEditDialog.EditMode.Edit, _loc, seed,
            catalogueContext: BuildCatalogueContext(),
            currentStock: row.CurrentStock,
            barcodeContext: BuildBarcodeScanContext());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<UpdateMedicine>();
            // Only the fields the user changed are written (B.1 Phase
            // 3a, stale forms): the seed is the baseline.
            await usecase.ExecuteAsync(
                dialog.Result.ToUpdateCommand(row.Id) with { Baseline = seed.ToUpdateCommand(row.Id) },
                CancellationToken.None);

            // If the user changed the schedule shape in the edit dialog,
            // record it as a new versioned schedule entry effective
            // today, preserving the timeline before that date.
            // UpdateMedicine deliberately does not touch the schedule,
            // so this is the path that persists it.
            //
            // Advanced mode → InitialSchedule is the chosen value object.
            // Simple mode → InitialSchedule is null; if the seed was an
            // advanced regime, the user switched back to a plain fixed
            // daily dose, which we persist as a FixedDailySchedule built
            // from the dose / frequency fields.
            var chosen = dialog.Result.InitialSchedule
                ?? new FixedDailySchedule(
                    dialog.Result.DosePerAdministration,
                    dialog.Result.AdministrationsPerDay);
            var seedSchedule = seed.InitialSchedule
                ?? new FixedDailySchedule(seed.DosePerAdministration, seed.AdministrationsPerDay);
            if (!chosen.Equals(seedSchedule))
            {
                var change = scope.ServiceProvider.GetRequiredService<ChangeMedicationSchedule>();
                var today = DateOnly.FromDateTime(DateTime.Today);
                var effectiveFrom = today < seed.StartDate ? seed.StartDate : today;
                var (displayDose, displayFreq) = ScheduleDisplayValues(chosen, seed);
                await change.ExecuteAsync(
                    new ChangeMedicationScheduleCommand(row.Id, displayDose, displayFreq, effectiveFrom, chosen),
                    CancellationToken.None);
            }

            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.UpdateMedicine"), ex);
        }
    }

    // Quick-glance dose / frequency stored on the Medicine row for a
    // non-FixedDaily schedule. Mirrors the display back-fill described
    // in docs/ANALYSIS-A1-STEPPED-TAPER.md §3.2 / ANALYSIS-A1-REGIMENS.md
    // §3.5 — never used by the projection, only for the summary column.
    private static (decimal Dose, int Freq) ScheduleDisplayValues(
        Schedule schedule, MedicineEditResult seed)
        => schedule switch
        {
            FixedDailySchedule f => (f.DosePerAdministration, f.AdministrationsPerDay),
            SteppedTaperingSchedule s => (s.Stages[0].Dose, 1),
            TaperingSchedule t => (t.StartDose, 1),
            CyclicSchedule c => (c.QuantityPerOnDay, 1),
            WeeklySchedule => (seed.DosePerAdministration, 1),
            PrnSchedule => (seed.DosePerAdministration, 1),
            _ => (seed.DosePerAdministration, seed.AdministrationsPerDay),
        };

    private async Task DeactivateSelectedAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var confirm = ConfirmDialog.Show(_loc, this,
            _loc.Get("Ui.MainForm.Deactivate.Confirm", row.Name),
            _loc.Get("Ui.MainForm.Deactivate.Confirm.Title"),
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var deactivate = scope.ServiceProvider.GetRequiredService<DeactivateMedicine>();
            var found = await deactivate.ExecuteAsync(
                new DeactivateMedicineCommand(row.Id), CancellationToken.None);
            if (!found) return;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.DeactivateMedicine"), ex);
        }
    }

    // Deletion of a medicine entered by mistake (DeleteMedicine). A
    // medicine with recorded facts is refused before the confirmation,
    // with the two ways out: deactivate, or retract the facts first.
    private async Task DeleteSelectedAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        try
        {
            DeleteMedicineOutcome check;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                check = await scope.ServiceProvider.GetRequiredService<DeleteMedicine>()
                    .CheckAsync(row.Id, CancellationToken.None);
            }
            if (check == DeleteMedicineOutcome.HasRecordedFacts)
            {
                ShowDeleteRefused(row.Name);
                return;
            }
            if (check == DeleteMedicineOutcome.NotFound)
            {
                await ReloadAsync();
                return;
            }

            var confirm = ConfirmDialog.Show(_loc, this,
                _loc.Get("Ui.MainForm.Delete.Confirm", row.Name),
                _loc.Get("Ui.MainForm.Delete.Title"),
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            DeleteMedicineOutcome outcome;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                outcome = await scope.ServiceProvider.GetRequiredService<DeleteMedicine>()
                    .ExecuteAsync(new DeleteMedicineCommand(row.Id), CancellationToken.None);
            }
            // A fact recorded meanwhile (a sync run, a reminder) wins.
            if (outcome == DeleteMedicineOutcome.HasRecordedFacts) ShowDeleteRefused(row.Name);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.DeleteMedicine"), ex);
        }
    }

    private void ShowDeleteRefused(string name)
        => MessageBox.Show(this,
            _loc.Get("Ui.MainForm.Delete.HasFacts", name),
            _loc.Get("Ui.MainForm.Delete.Title"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);

    private async Task ShowChangeScheduleAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        decimal currentDose;
        int currentFreq;
        DateOnly startDate;
        Schedule? currentSchedule = null;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;
            currentDose = medicine.DosePerAdministration;
            currentFreq = medicine.AdministrationsPerDay;
            startDate = medicine.StartDate;

            // Reconstruct the therapy's current schedule from the most
            // recent history entry so the dialog opens pre-populated
            // with an existing advanced regime instead of resetting to
            // Simple. FixedDaily reconstructs to a FixedDailySchedule,
            // which the dialog treats as Simple mode.
            var schedules = scope.ServiceProvider
                .GetRequiredService<IMedicationScheduleHistoryRepository>();
            var history = await schedules.ListForMedicineAsync(row.Id, CancellationToken.None);
            MedicationScheduleHistory? latest = null;
            foreach (var entry in history)
            {
                if (latest is null || entry.EffectiveFrom > latest.EffectiveFrom)
                {
                    latest = entry;
                }
            }
            if (latest is not null)
            {
                currentSchedule = ScheduleCodec.Deserialize(
                    latest.ScheduleKind,
                    latest.SchedulePayload,
                    latest.DosePerAdministration,
                    latest.AdministrationsPerDay);
            }
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new ChangeScheduleDialog(row.Name, currentDose, currentFreq, startDate, _loc, currentSchedule);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<ChangeMedicationSchedule>();
            await usecase.ExecuteAsync(dialog.Result.ToCommand(row.Id), CancellationToken.None);
            _log.LogInformation("Cambio schedule per medicina {MedicineId}: dose {Dose}, freq {Freq}, dal {From}",
                row.Id, dialog.Result.NewDose, dialog.Result.NewFreq, dialog.Result.EffectiveFrom);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ChangeSchedule"), ex);
        }
    }

    private async Task ShowRegisterIntakeAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        // The dialog's default dose is the medicine's current one.
        decimal suggestedQuantity;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;
            suggestedQuantity = medicine.DosePerAdministration;
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new IntakeDialog(row.Name, row.Unit, suggestedQuantity, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<RegisterIntake>();
            await usecase.ExecuteAsync(dialog.Result.ToCommand(row.Id), CancellationToken.None);
            _log.LogInformation("Intake recorded for medicine {MedicineId}: {Status} {Quantity} on {Day}",
                row.Id, dialog.Result.Status, dialog.Result.Quantity, dialog.Result.Day);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.RegisterIntake"), ex);
        }
    }

    private async Task ShowStockDialogAsync(StockOperationKind defaultKind, decimal? initialQuantity = null)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        using var dialog = new StockAdjustmentDialog(
            row.Name, row.CurrentStock, row.Unit, defaultKind, _loc, initialQuantity);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (dialog.Result.IsPositive)
            {
                var addStock = scope.ServiceProvider.GetRequiredService<AddStock>();
                await addStock.ExecuteAsync(
                    new AddStockCommand(row.Id, dialog.Result.Quantity, dialog.Result.ToMovementKind(), dialog.Result.Notes),
                    CancellationToken.None);
            }
            else
            {
                var adjust = scope.ServiceProvider.GetRequiredService<AdjustStockDown>();
                await adjust.ExecuteAsync(
                    new AdjustStockDownCommand(row.Id, dialog.Result.Quantity, dialog.Result.Notes),
                    CancellationToken.None);
            }
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.StockMovement"), ex);
        }
    }

    // Restock by scan (A2 phase 3, flow b): the scanned national code
    // identifies the medicine, then the usual new-package dialog opens
    // with the quantity of its last new package. A code that matches no
    // medicine can be linked to an existing one or added as a new one.
    // See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5C.
    private async Task RestockFromBarcodeAsync()
    {
        var barcode = BuildBarcodeScanContext();
        BarcodeContent? content;
        using (var dialog = new BarcodeScanDialog(
            _loc, barcode.Parser, barcode.Camera, barcode.Options, barcode.Logger))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            content = dialog.Result;
        }
        if (content?.LookupKey is not { } code) return;

        // A GTIN alone does not identify a medicine of the profile:
        // medicines carry the national code only (§5C.3).
        if (content.NationalCode is not { } nationalCode)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MainForm.RestockScan.NoNationalCode", code),
                _loc.Get("Common.Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            IReadOnlyList<RestockCandidate> candidates;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                candidates = await scope.ServiceProvider.GetRequiredService<RestockByScanQuery>()
                    .FindByNationalCodeAsync(nationalCode, CancellationToken.None);
            }
            _log.LogInformation("Restock by scan: {Count} medicine(s) match the scanned code.", candidates.Count);

            var candidate = candidates.Count switch
            {
                0 => await ResolveUnmatchedCodeAsync(nationalCode),
                1 => candidates[0],
                _ => PickMedicine(
                    _loc.Get("Ui.MainForm.RestockScan.PickTitle"),
                    _loc.Get("Ui.MainForm.RestockScan.PickPrompt", nationalCode),
                    candidates),
            };
            if (candidate is null) return;

            SelectGridRow(candidate.MedicineId);
            if (GetSelectedRow()?.Id != candidate.MedicineId) return;
            await ShowStockDialogAsync(StockOperationKind.NewPackage, candidate.LastNewPackageQuantity);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.RestockScan"), ex);
        }
    }

    // No medicine carries the code. When the catalogue knows it, offer
    // to add it as a new medicine (the edit dialog opens filled in from
    // the catalogue, with its own initial quantity, so no restock
    // follows) or to link it to a medicine that has no code yet, which
    // is then restocked. Returns the medicine to restock, or null.
    private async Task<RestockCandidate?> ResolveUnmatchedCodeAsync(string nationalCode)
    {
        var catalogue = BuildCatalogueContext();
        var reference = catalogue is null
            ? null
            : await LookupReferenceByNationalCodeAsync(catalogue.Country, nationalCode, CancellationToken.None);
        if (catalogue is null || reference is null)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MainForm.RestockScan.NotFound", nationalCode),
                _loc.Get("Common.Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var addNew = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.MainForm.RestockScan.AddNew"), _loc.Get("Ui.MainForm.RestockScan.AddNew.Description"));
        var link = new TaskDialogCommandLinkButton(
            _loc.Get("Ui.MainForm.RestockScan.Link"), _loc.Get("Ui.MainForm.RestockScan.Link.Description"));
        var page = new TaskDialogPage
        {
            Caption = _loc.Get("Ui.MainForm.RestockScan.PickTitle"),
            Heading = _loc.Get("Ui.MainForm.RestockScan.NoMatch.Heading", nationalCode),
            Text = _loc.Get("Ui.MainForm.RestockScan.NoMatch.Text", reference.CommercialName),
            Icon = TaskDialogIcon.Information,
            Buttons = { addNew, link, TaskDialogButton.Cancel },
        };
        var choice = TaskDialog.ShowDialog(this, page);

        if (choice == addNew)
        {
            await ShowNewMedicineAsync(reference);
            return null;
        }
        if (choice != link) return null;

        IReadOnlyList<RestockCandidate> unlinked;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            unlinked = await scope.ServiceProvider.GetRequiredService<RestockByScanQuery>()
                .ListUnlinkedAsync(CancellationToken.None);
        }
        if (unlinked.Count == 0)
        {
            MessageBox.Show(this,
                _loc.Get("Ui.MainForm.RestockScan.NoUnlinked"),
                _loc.Get("Common.Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var picked = PickMedicine(
            _loc.Get("Ui.MainForm.RestockScan.LinkTitle"),
            _loc.Get("Ui.MainForm.RestockScan.LinkPrompt", reference.CommercialName),
            unlinked);
        if (picked is null) return null;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var linkUseCase = scope.ServiceProvider.GetRequiredService<LinkMedicineToReferenceUseCase>();
            var result = await linkUseCase.ExecuteAsync(
                picked.MedicineId, catalogue.Country, nationalCode, CancellationToken.None);
            if (result != LinkResult.Linked) return null;
        }
        _log.LogInformation("Restock by scan: medicine {MedicineId} linked to the scanned code.", picked.MedicineId);
        await ReloadAsync();
        return picked;
    }

    private RestockCandidate? PickMedicine(string title, string prompt, IReadOnlyList<RestockCandidate> candidates)
    {
        using var picker = new MedicinePickerDialog(title, prompt, candidates, _loc);
        return picker.ShowDialog(this) == DialogResult.OK ? picker.Result : null;
    }

    private async Task ShowStockCountAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        StockCountSnapshot snapshot;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reconcile = scope.ServiceProvider.GetRequiredService<ReconcileStock>();
            snapshot = await reconcile.LoadAsync(row.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new StockCountDialog(row.Name, row.Unit, snapshot, _loc);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reconcile = scope.ServiceProvider.GetRequiredService<ReconcileStock>();
            var result = await reconcile.ExecuteAsync(
                dialog.Result.ToCommand(row.Id, _loc.Get("Ui.StockCountDialog.DefaultNote")),
                CancellationToken.None);
            _log.LogInformation(
                "Stock count for medicine {MedicineId}: gap {Gap}, correction {Correction} ({Kind}), epoch advanced {EpochAdvanced}, {Days} consumption days materialized",
                row.Id, result.Gap, result.Correction, result.CorrectionKind?.ToString() ?? "none",
                result.StockEpochAdvanced, result.ConsumptionDaysMaterialized);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.StockCount"), ex);
        }
    }

    // History of the facts of the selected medicine, with retraction of
    // mistaken entries (B.1 Phase 2d). Each call opens its own scope, as
    // the other dialogs do.
    private async Task ShowFactHistoryAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        async Task<IReadOnlyList<FactHistoryItem>> LoadAsync()
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FactHistoryQuery>()
                .LoadAsync(row.Id, CancellationToken.None);
        }

        async Task RetractAsync(FactHistoryItem item)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RetractFact>()
                .ExecuteAsync(new RetractFactCommand(row.Id, item.Kind, item.FactId), CancellationToken.None);
            _log.LogInformation("Retracted {Kind} fact {FactId} of medicine {MedicineId}",
                item.Kind, item.FactId, row.Id);
        }

        using var dialog = new FactHistoryDialog(row.Name, row.Unit, LoadAsync, RetractAsync, _loc);
        dialog.ShowDialog(this);
        if (dialog.Changed)
        {
            await ReloadAsync();
        }
    }

    private async Task RunMonitorAsync()
    {
        SetStatus(_loc.Get("Ui.MainForm.Status.CheckRunning"));
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var catchUp = scope.ServiceProvider.GetRequiredService<ConsumptionCatchUp>();
            var monitor = scope.ServiceProvider.GetRequiredService<MedicationMonitor>();
            await catchUp.RunAsync(CancellationToken.None);
            var result = await monitor.RunAsync(CancellationToken.None);
            SetStatus(_loc.Get("Ui.MainForm.Status.CheckCompleted",
                result.MedicinesInspected, result.NotificationsSent));
            _lastCheckLabel.Text = _loc.Get("Ui.MainForm.LastCheck.At", DateTime.Now.ToString("HH:mm"));
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.MonitorCheck"), ex);
        }
    }

    private void ShowSettings()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var dialog = new SettingsDialog(
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<SmtpSettings>>(),
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<NotificationSettings>>(),
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BackupSettings>>(),
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<UserSettings>>(),
                scope.ServiceProvider.GetRequiredService<ISmtpCredentialStore>(),
                scope.ServiceProvider.GetRequiredService<IEmailNotificationService>(),
                scope.ServiceProvider.GetRequiredService<IAutoStartService>(),
                scope.ServiceProvider.GetRequiredService<IBackupService>(),
                scope.ServiceProvider.GetRequiredService<IBackupStateStore>(),
                scope.ServiceProvider.GetRequiredService<IApplicationRestarter>(),
                scope.ServiceProvider.GetRequiredService<ICurrentProfile>(),
                scope.ServiceProvider.GetRequiredService<IProfileRegistry>(),
                scope.ServiceProvider.GetRequiredService<ILocalizationService>(),
                scope.ServiceProvider.GetRequiredService<MedReminder.Application.Catalogue.IReferenceCatalogueQueryService>(),
                scope.ServiceProvider.GetRequiredService<MedReminder.Application.Export.IExportService>(),
                scope.ServiceProvider.GetRequiredService<MedReminder.Application.Export.IImportService>(),
                scope.ServiceProvider.GetRequiredService<ICloudBackupPassphraseStore>(),
                scope.ServiceProvider.GetRequiredService<MedReminder.Application.Export.ICloudRestoreService>(),
                scope.ServiceProvider.GetRequiredService<SyncHostedService>(),
                scope.ServiceProvider.GetRequiredService<ICloudAccountService>(),
                scope.ServiceProvider.GetRequiredService<IArchiveStorage>(),
                _scopeFactory);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.OpenSettings"), ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _log.LogError(ex, "{Title}", title);
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
