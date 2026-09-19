using System.ComponentModel;
using System.Reflection;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UpdateChecking;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Email;
using MedReminder.UI.Presentation;
using MedReminder.UI.Tray;
using MedReminder.UI.UiExtensions;
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
    // Soft backgrounds applied to the whole row: "atmosphere"
    // layer that hints at the state without overloading the view.
    private static readonly Color WarningColor = Color.FromArgb(255, 245, 205);
    private static readonly Color EmptyColor = Color.FromArgb(255, 210, 210);
    private static readonly Color SuspendedColor = Color.FromArgb(230, 230, 230);

    // "Status" cell: saturated badge with high-contrast bold text.
    // "Signal" layer — legible at a glance even when the row is not
    // focused. Material light 200 / 900 palette to guarantee WCAG
    // AA contrast on the foreground colors.
    private static readonly Color StatusOkBack       = Color.FromArgb(200, 230, 201);  // #C8E6C9
    private static readonly Color StatusOkFore       = Color.FromArgb( 27,  94,  32);  // #1B5E20
    private static readonly Color StatusWarnBack     = Color.FromArgb(255, 236, 179);  // #FFECB3
    private static readonly Color StatusWarnFore     = Color.FromArgb( 93,  64,  55);  // #5D4037
    private static readonly Color StatusEmptyBack    = Color.FromArgb(239, 154, 154);  // #EF9A9A
    private static readonly Color StatusEmptyFore    = Color.FromArgb(183,  28,  28);  // #B71C1C
    private static readonly Color StatusSuspendBack  = Color.FromArgb(207, 207, 207);  // #CFCFCF
    private static readonly Color StatusSuspendFore  = Color.FromArgb( 66,  66,  66);  // #424242

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApplicationTrayIcon _tray;
    private readonly ILogger<MainForm> _log;
    private readonly ILocalizationService _loc;
    private readonly ICurrentProfile _currentProfile;
    private readonly IProfileRegistry _profileRegistry;
    private readonly IApplicationRestarter _restarter;

    private DataGridView _grid = null!;
    private BindingList<MedicineListItem> _rows = [];
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

    public MainForm(
        IServiceScopeFactory scopeFactory,
        ApplicationTrayIcon tray,
        ILogger<MainForm> log,
        ILocalizationService localization,
        ICurrentProfile currentProfile,
        IProfileRegistry profileRegistry,
        IApplicationRestarter restarter)
    {
        _scopeFactory = scopeFactory;
        _tray = tray;
        _log = log;
        _loc = localization;
        _currentProfile = currentProfile;
        _profileRegistry = profileRegistry;
        _restarter = restarter;

        // Title bar shows the active profile so multi-profile users
        // can always see which one is open (§12.1).
        Text = _loc.Get("Ui.MainForm.Title.WithProfile",
            _loc.Get("Ui.MainForm.Title"), _currentProfile.DisplayName);
        Width = 960;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.75F);
        MinimumSize = new Size(720, 420);

        BuildLayout();
        WireTrayHandlers();

        Load += async (_, _) => await ReloadAsync();
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

        // TableLayout with 5 rows: menu, toolbar, error banner,
        // grid, status. The MenuStrip must be added to Controls AND
        // assigned to MainMenuStrip so keyboard shortcuts (Ctrl+N,
        // F5, Alt+F4) work everywhere.
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // menu
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // toolbar
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // banner (Visible=false)
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// grid
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
        container.Controls.Add(menuStrip, 0, 0);
        container.Controls.Add(toolStrip, 0, 1);
        container.Controls.Add(_errorBanner, 0, 2);
        container.Controls.Add(_grid, 0, 3);
        container.Controls.Add(statusStrip, 0, 4);

        Controls.Add(container);
        MainMenuStrip = menuStrip;
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
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.RegisterIntake"),
            Mdl2Glyph.Glyphs.CheckMark, Keys.Control | Keys.I,
            async () => await ShowRegisterIntakeAsync()));
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Therapy.Report"),
            Mdl2Glyph.Glyphs.Document, Keys.Control | Keys.P,
            async () => await ShowTherapyReportAsync()));

        // Scorte
        var stockMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Stock"));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.AddPackage"),
            Mdl2Glyph.Glyphs.Package, Keys.Control | Keys.Shift | Keys.A,
            async () => await ShowStockDialogAsync(StockOperationKind.NewPackage)));
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Adjust"),
            Mdl2Glyph.Glyphs.Warning, Keys.None,
            async () => await ShowStockDialogAsync(StockOperationKind.NegativeCorrection)));
        stockMenu.DropDownItems.Add(new ToolStripSeparator());
        stockMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Stock.Refresh"),
            Mdl2Glyph.Glyphs.Refresh, Keys.F5,
            async () => await ReloadAsync()));

        // Strumenti
        var toolsMenu = new ToolStripMenuItem(_loc.Get("Ui.MainForm.Menu.Tools"));
        toolsMenu.DropDownItems.Add(BuildMenuItem(_loc.Get("Ui.MainForm.Menu.Tools.CheckNow"),
            Mdl2Glyph.Glyphs.Sync, Keys.Control | Keys.R,
            async () => await RunMonitorAsync()));
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

        var strip = new MenuStrip { Dock = DockStyle.Top };
        strip.Items.Add(fileMenu);
        strip.Items.Add(therapyMenu);
        strip.Items.Add(stockMenu);
        strip.Items.Add(toolsMenu);
        strip.Items.Add(helpMenu);
        return strip;
    }

    private static ToolStripMenuItem BuildMenuItem(
        string text, string glyph, Keys shortcut, Func<Task> action)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = Mdl2Glyph.Create(glyph, size: 16),
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
    private void TryStartPassiveUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
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
            BackColor = Color.FromArgb(255, 220, 220),
            Padding = new Padding(12, 8, 12, 8),
            Visible = false,
        };

        _errorBannerLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            ForeColor = Color.FromArgb(120, 0, 0),
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

    // Toolbar "quick access": solo 5 azioni frequenti, icone MDL2 sopra
    // il testo. Tutti gli altri comandi sono raggiungibili dal MenuStrip
    // + shortcut tastiera. Riduce il rumore visivo (era ~11 bottoni).
    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(6, 4, 6, 4),
            ImageScalingSize = new Size(24, 24),
            AutoSize = true,
        };
        strip.Items.Add(BuildToolbarButton(_loc.Get("Ui.MainForm.Toolbar.NewMedicine"),
            Mdl2Glyph.Glyphs.Add,
            async () => await ShowNewMedicineAsync()));
        strip.Items.Add(BuildToolbarButton(_loc.Get("Ui.MainForm.Toolbar.Edit"),
            Mdl2Glyph.Glyphs.Edit,
            async () => await ShowEditMedicineAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(BuildToolbarButton(_loc.Get("Ui.MainForm.Toolbar.RegisterIntake"),
            Mdl2Glyph.Glyphs.CheckMark,
            async () => await ShowRegisterIntakeAsync()));
        strip.Items.Add(BuildToolbarButton(_loc.Get("Ui.MainForm.Toolbar.CheckNow"),
            Mdl2Glyph.Glyphs.Sync,
            async () => await RunMonitorAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(BuildToolbarButton(_loc.Get("Ui.MainForm.Toolbar.TherapyReport"),
            Mdl2Glyph.Glyphs.Document,
            async () => await ShowTherapyReportAsync()));
        return strip;
    }

    private static ToolStripButton BuildToolbarButton(
        string text, string glyph, Func<Task> action)
    {
        var b = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageAboveText,
            Image = Mdl2Glyph.Create(glyph, size: 24),
            ImageScaling = ToolStripItemImageScaling.None,
            AutoSize = true,
            Padding = new Padding(4, 2, 4, 2),
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

            var reportText = MedReminder.Application.Reporting.TherapyReport.Build(entries, today, _loc.CurrentCulture, _loc);
            using var dialog = new TherapyReportDialog(reportText, _loc);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.GenerateReport"), ex);
        }
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
            ForeColor = _currentProfile.IsAdmin
                ? System.Drawing.Color.DarkBlue
                : System.Drawing.Color.Black,
            Font = new Font("Segoe UI", 9.75F,
                _currentProfile.IsAdmin ? FontStyle.Bold : FontStyle.Regular),
        };
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
                _profileRegistry, _currentProfile, _loc);
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
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Medicine"),
            DataPropertyName = nameof(MedicineListItem.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Stock"),
            DataPropertyName = nameof(MedicineListItem.StockDisplay),
            Width = 120,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.DailyRate"),
            DataPropertyName = nameof(MedicineListItem.DailyRateDisplay),
            Width = 100,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.DaysRemaining"),
            DataPropertyName = nameof(MedicineListItem.DaysRemainingDisplay),
            Width = 100,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.RunOut"),
            DataPropertyName = nameof(MedicineListItem.EtaDisplay),
            Width = 110,
        });
        _statusCellFont = new Font(Font, FontStyle.Bold);
        var statusColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = _loc.Get("Ui.MainForm.Column.Status"),
            DataPropertyName = nameof(MedicineListItem.StatusDisplay),
            Width = 110,
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
        grid.RowPrePaint += OnRowPrePaint;
        grid.CellDoubleClick += async (_, _) => await ShowEditMedicineAsync();
        return grid;
    }

    private void OnRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
        var row = _grid.Rows[e.RowIndex];
        var item = _rows[e.RowIndex];

        // Layer 1 — atmosphere across the whole row (soft colors).
        row.DefaultCellStyle.BackColor = item.Status switch
        {
            MedicineRowStatus.Warning => WarningColor,
            MedicineRowStatus.Empty => EmptyColor,
            MedicineRowStatus.Suspended => SuspendedColor,
            _ => SystemColors.Window,
        };

        // Layer 2 — badge on the "Status" cell (saturated bg + fg).
        // SelectionBackColor / SelectionForeColor mirror the badge
        // so the signal survives even when the row is selected.
        if (_statusColumnIndex < 0 || _statusColumnIndex >= row.Cells.Count) return;
        var (bg, fg) = item.Status switch
        {
            MedicineRowStatus.Ok        => (StatusOkBack,      StatusOkFore),
            MedicineRowStatus.Warning   => (StatusWarnBack,    StatusWarnFore),
            MedicineRowStatus.Empty     => (StatusEmptyBack,   StatusEmptyFore),
            MedicineRowStatus.Suspended => (StatusSuspendBack, StatusSuspendFore),
            _                           => (SystemColors.Window, SystemColors.ControlText),
        };
        var cell = row.Cells[_statusColumnIndex];
        cell.Style.BackColor = bg;
        cell.Style.ForeColor = fg;
        cell.Style.SelectionBackColor = bg;
        cell.Style.SelectionForeColor = fg;
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

            _rows = new BindingList<MedicineListItem>(items.ToList());
            _grid.DataSource = _rows;
            SetStatus(_loc.Get("Ui.MainForm.Status.MedicinesLoaded", items.Count));
            HideErrorBanner();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore caricamento medicine");
            SetStatus(_loc.Get("Ui.MainForm.Status.LoadError"));
            ShowErrorBanner(_loc.Get("Ui.MainForm.LoadBanner.Failure", ex.Message));
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

    private async Task ShowNewMedicineAsync()
    {
        using var dialog = new MedicineEditDialog(
            MedicineEditDialog.EditMode.Create, _loc,
            catalogueContext: BuildCatalogueContext());
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

            seed = new MedicineEditResult(
                medicine.Name, medicine.ActiveIngredient, medicine.Package, medicine.Unit,
                medicine.DosePerAdministration, medicine.AdministrationsPerDay,
                medicine.StartDate, medicine.EndDate, medicine.ThresholdDays,
                medicine.DoctorName, medicine.Notes,
                InitialQuantity: 0m, medicine.NotificationChannels, medicine.IsActive,
                Slots: seedSlots,
                NationalCode: medicine.NationalCode,
                AtcCode: medicine.AtcCode,
                LinkedReferenceMedicineId: medicine.LinkedReferenceMedicineId);
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new MedicineEditDialog(
            MedicineEditDialog.EditMode.Edit, _loc, seed,
            catalogueContext: BuildCatalogueContext());
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<UpdateMedicine>();
            await usecase.ExecuteAsync(dialog.Result.ToUpdateCommand(row.Id), CancellationToken.None);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.UpdateMedicine"), ex);
        }
    }

    private async Task DeactivateSelectedAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var confirm = MessageBox.Show(this,
            _loc.Get("Ui.MainForm.Deactivate.Confirm", row.Name),
            _loc.Get("Ui.MainForm.Deactivate.Confirm.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;
            medicine.IsActive = false;
            medicine.UpdatedAt = DateTimeOffset.UtcNow;
            await repo.UpdateAsync(medicine, CancellationToken.None);
            await uow.SaveChangesAsync(CancellationToken.None);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.DeactivateMedicine"), ex);
        }
    }

    private async Task ShowChangeScheduleAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        decimal currentDose;
        int currentFreq;
        DateOnly startDate;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMedicineRepository>();
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;
            currentDose = medicine.DosePerAdministration;
            currentFreq = medicine.AdministrationsPerDay;
            startDate = medicine.StartDate;
        }
        catch (Exception ex)
        {
            ShowError(_loc.Get("Ui.MainForm.Error.ReadMedicine"), ex);
            return;
        }

        using var dialog = new ChangeScheduleDialog(row.Name, currentDose, currentFreq, startDate, _loc);
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

    private async Task ShowStockDialogAsync(StockOperationKind defaultKind)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        using var dialog = new StockAdjustmentDialog(row.Name, row.CurrentStock, row.Unit, defaultKind, _loc);
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
                scope.ServiceProvider.GetRequiredService<MedReminder.Application.Catalogue.IReferenceCatalogueQueryService>());
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
