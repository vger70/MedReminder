using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Email;
using MedReminder.UI.Presentation;
using MedReminder.UI.Tray;
using MedReminder.UI.UiExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Finestra principale: elenco medicine, toolbar, integrazione tray.
// Le operazioni asincrone creano una scope DI dedicata via
// IServiceScopeFactory — il DbContext è Scoped, non deve essere
// condiviso tra thread o operazioni concorrenti.
internal sealed class MainForm : MedReminderFormBase
{
    // Sfondi tenui applicati a tutta la riga: layer "atmosfera" che
    // suggerisce lo stato senza sovraccaricare la vista.
    private static readonly Color WarningColor = Color.FromArgb(255, 245, 205);
    private static readonly Color EmptyColor = Color.FromArgb(255, 210, 210);
    private static readonly Color SuspendedColor = Color.FromArgb(230, 230, 230);

    // Cella "Stato": badge saturo con testo bold contrastato. Layer
    // "segnale" — leggibile a colpo d'occhio anche se la riga non è
    // in focus. Palette Material light 200/900 per garantire un
    // contrasto WCAG AA sui foreground.
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

    private DataGridView _grid = null!;
    private BindingList<MedicineListItem> _rows = new();
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _lastCheckLabel = null!;
    private Panel _errorBanner = null!;
    private Label _errorBannerLabel = null!;

    // Indice della colonna "Stato" per applicare i colori badge nel
    // RowPrePaint senza dover cercare la colonna per nome ogni volta.
    private int _statusColumnIndex = -1;
    // Font bold cache-ato per le celle dello Stato: creare un Font
    // nuovo ad ogni prepaint sarebbe sprecato.
    private Font? _statusCellFont;

    private bool _closeToTray = true;
    private bool _reallyExit;

    public MainForm(IServiceScopeFactory scopeFactory, ApplicationTrayIcon tray, ILogger<MainForm> log)
    {
        _scopeFactory = scopeFactory;
        _tray = tray;
        _log = log;

        Text = "MedReminder";
        Width = 960;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.75F);
        MinimumSize = new Size(720, 420);

        BuildLayout();
        WireTrayHandlers();

        Load += async (_, _) => await ReloadAsync();
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var menuStrip = BuildMenuStrip();
        var toolStrip = BuildToolStrip();
        var statusStrip = BuildStatusStrip();
        _grid = BuildGrid();
        _errorBanner = BuildErrorBanner();

        // TableLayout in 5 righe: menu, toolbar, banner errore, grid, status.
        // Il MenuStrip va aggiunto a Controls e assegnato a MainMenuStrip
        // così i keyboard shortcut (Ctrl+N, F5, Alt+F4) funzionano ovunque.
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
        // Nota: NON registro Alt+F4 come ShortcutKeys — Alt+F4 è già
        // gestito dall'OS (chiude/nasconde la finestra) e il tray-hide
        // di OnFormClosing è il comportamento voluto in quel caso.
        // "Esci" (Ctrl+Q) forza invece l'uscita reale via _reallyExit.
        var fileMenu = new ToolStripMenuItem("&File");
        var fileExit = new ToolStripMenuItem("Esci", null,
            (_, _) => { _reallyExit = true; Close(); })
        { ShortcutKeys = Keys.Control | Keys.Q };
        fileMenu.DropDownItems.Add(fileExit);

        // Terapia
        var therapyMenu = new ToolStripMenuItem("&Terapia");
        therapyMenu.DropDownItems.Add(BuildMenuItem("&Nuova medicina…",
            Mdl2Glyph.Glyphs.Add, Keys.Control | Keys.N,
            async () => await ShowNewMedicineAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem("&Modifica",
            Mdl2Glyph.Glyphs.Edit, Keys.F2,
            async () => await ShowEditMedicineAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem("Cambia &dose/frequenza…",
            Mdl2Glyph.Glyphs.Notebook, Keys.None,
            async () => await ShowChangeScheduleAsync()));
        therapyMenu.DropDownItems.Add(BuildMenuItem("&Disattiva",
            Mdl2Glyph.Glyphs.Cancel, Keys.None,
            async () => await DeactivateSelectedAsync()));
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem("&Registra assunzione…",
            Mdl2Glyph.Glyphs.CheckMark, Keys.Control | Keys.I,
            async () => await ShowRegisterIntakeAsync()));
        therapyMenu.DropDownItems.Add(new ToolStripSeparator());
        therapyMenu.DropDownItems.Add(BuildMenuItem("&Scheda terapia…",
            Mdl2Glyph.Glyphs.Document, Keys.Control | Keys.P,
            async () => await ShowTherapyReportAsync()));

        // Scorte
        var stockMenu = new ToolStripMenuItem("&Scorte");
        stockMenu.DropDownItems.Add(BuildMenuItem("&Aggiungi confezione…",
            Mdl2Glyph.Glyphs.Package, Keys.Control | Keys.Shift | Keys.A,
            async () => await ShowStockDialogAsync(StockOperationKind.NewPackage)));
        stockMenu.DropDownItems.Add(BuildMenuItem("&Correggi scorte…",
            Mdl2Glyph.Glyphs.Warning, Keys.None,
            async () => await ShowStockDialogAsync(StockOperationKind.NegativeCorrection)));
        stockMenu.DropDownItems.Add(new ToolStripSeparator());
        stockMenu.DropDownItems.Add(BuildMenuItem("A&ggiorna elenco",
            Mdl2Glyph.Glyphs.Refresh, Keys.F5,
            async () => await ReloadAsync()));

        // Strumenti
        var toolsMenu = new ToolStripMenuItem("Str&umenti");
        toolsMenu.DropDownItems.Add(BuildMenuItem("&Controlla ora",
            Mdl2Glyph.Glyphs.Sync, Keys.Control | Keys.R,
            async () => await RunMonitorAsync()));
        toolsMenu.DropDownItems.Add(new ToolStripSeparator());
        toolsMenu.DropDownItems.Add(BuildMenuItem("&Impostazioni…",
            Mdl2Glyph.Glyphs.Settings, Keys.Control | Keys.Oemcomma,
            () => { ShowSettings(); return Task.CompletedTask; }));

        // Aiuto (?)
        var helpMenu = new ToolStripMenuItem("&?");
        var helpGuide = BuildMenuItem("&Guida utente",
            Mdl2Glyph.Glyphs.Help, Keys.F1,
            () => { OpenUserGuide(); return Task.CompletedTask; });
        var helpAbout = BuildMenuItem("&Info su MedReminder…",
            Mdl2Glyph.Glyphs.Info, Keys.None,
            () => { ShowAboutDialog(); return Task.CompletedTask; });
        helpMenu.DropDownItems.Add(helpGuide);
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
            using var dialog = new HelpViewerForm();
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError("Impossibile aprire la guida", ex);
        }
    }

    private void ShowAboutDialog()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        var message =
            $"MedReminder\nVersione {version}\n\n" +
            "Promemoria organizzativo per scorte di medicine — non è un dispositivo medico " +
            "e non fornisce indicazioni cliniche.\n\n" +
            "Sorgente: https://github.com/vger70/MedReminder";
        MessageBox.Show(this, message, "Info su MedReminder",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Banner rosso che appare in cima alla griglia quando ReloadAsync
    // fallisce. Include un pulsante "Riprova" perché lo status label a
    // fondo pagina è troppo poco visibile per un errore che blocca la
    // funzionalità principale (spec §19: errore non fatale, UI deve
    // comunque comunicarlo).
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
            Text = "Errore nel caricamento. Vedi log.",
        };

        var retryButton = new Button
        {
            Text = "Riprova",
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
        strip.Items.Add(BuildToolbarButton("Nuova\nmedicina",
            Mdl2Glyph.Glyphs.Add,
            async () => await ShowNewMedicineAsync()));
        strip.Items.Add(BuildToolbarButton("Modifica",
            Mdl2Glyph.Glyphs.Edit,
            async () => await ShowEditMedicineAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(BuildToolbarButton("Registra\nassunzione",
            Mdl2Glyph.Glyphs.CheckMark,
            async () => await ShowRegisterIntakeAsync()));
        strip.Items.Add(BuildToolbarButton("Controlla\nora",
            Mdl2Glyph.Glyphs.Sync,
            async () => await RunMonitorAsync()));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(BuildToolbarButton("Scheda\nterapia",
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

            var reportText = MedReminder.Application.Reporting.TherapyReport.Build(entries, today);
            using var dialog = new TherapyReportDialog(reportText);
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError("Errore generazione scheda terapia", ex);
        }
    }

    private StatusStrip BuildStatusStrip()
    {
        _statusLabel = new ToolStripStatusLabel("Pronto.")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _lastCheckLabel = new ToolStripStatusLabel("Ultimo controllo: —")
        {
            TextAlign = ContentAlignment.MiddleRight,
        };
        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.Add(_statusLabel);
        strip.Items.Add(_lastCheckLabel);
        return strip;
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
            HeaderText = "Medicina",
            DataPropertyName = nameof(MedicineListItem.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Scorta",
            DataPropertyName = nameof(MedicineListItem.StockDisplay),
            Width = 120,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Consumo/gg",
            DataPropertyName = nameof(MedicineListItem.DailyRateDisplay),
            Width = 100,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Giorni residui",
            DataPropertyName = nameof(MedicineListItem.DaysRemainingDisplay),
            Width = 100,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Esaurimento",
            DataPropertyName = nameof(MedicineListItem.EtaDisplay),
            Width = 110,
        });
        _statusCellFont = new Font(Font, FontStyle.Bold);
        var statusColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Stato",
            DataPropertyName = nameof(MedicineListItem.StatusDisplay),
            Width = 110,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                // Alignment e font sono livello-colonna: non cambiano per
                // riga, così li impostiamo qui una volta. I colori dello
                // stato invece variano e vanno applicati in RowPrePaint.
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

        // Layer 1 — atmosfera sulla riga intera (colori tenui).
        row.DefaultCellStyle.BackColor = item.Status switch
        {
            MedicineRowStatus.Warning => WarningColor,
            MedicineRowStatus.Empty => EmptyColor,
            MedicineRowStatus.Suspended => SuspendedColor,
            _ => SystemColors.Window,
        };

        // Layer 2 — badge sulla cella "Stato" (bg + fg saturi).
        // SelectionBackColor/SelectionForeColor uguali al badge per
        // preservare il segnale anche quando la riga è selezionata.
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
        SetStatus("Caricamento…");
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var loader = scope.ServiceProvider.GetRequiredService<MedicineOverviewLoader>();
            var items = await loader.LoadAsync(CancellationToken.None);

            _rows = new BindingList<MedicineListItem>(items.ToList());
            _grid.DataSource = _rows;
            SetStatus($"{items.Count} medicine caricate.");
            HideErrorBanner();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Errore caricamento medicine");
            SetStatus("Errore caricamento — vedi log.");
            ShowErrorBanner($"Impossibile caricare le medicine: {ex.Message}");
        }
    }

    private MedicineListItem? GetSelectedRow()
    {
        if (_grid.SelectedRows.Count == 0) return null;
        return _grid.SelectedRows[0].DataBoundItem as MedicineListItem;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    // ------------------ Actions ------------------
    private async Task ShowNewMedicineAsync()
    {
        using var dialog = new MedicineEditDialog(MedicineEditDialog.EditMode.Create);
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
            ShowError("Errore creazione medicina", ex);
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
            IReadOnlyList<AdministrationSlotEntry> seedSlots = slots
                .Select(s => new AdministrationSlotEntry(s.Time, s.Dose, s.TimingLabel))
                .ToList();

            seed = new MedicineEditResult(
                medicine.Name, medicine.ActiveIngredient, medicine.Package, medicine.Unit,
                medicine.DosePerAdministration, medicine.AdministrationsPerDay,
                medicine.StartDate, medicine.EndDate, medicine.ThresholdDays,
                medicine.DoctorName, medicine.Notes,
                InitialQuantity: 0m, medicine.NotificationChannels, medicine.IsActive,
                Slots: seedSlots);
        }
        catch (Exception ex)
        {
            ShowError("Errore lettura medicina", ex);
            return;
        }

        using var dialog = new MedicineEditDialog(MedicineEditDialog.EditMode.Edit, seed);
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
            ShowError("Errore aggiornamento", ex);
        }
    }

    private async Task DeactivateSelectedAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        var confirm = MessageBox.Show(this,
            $"Disattivare '{row.Name}'?\nI dati storici verranno preservati; la medicina non genererà più avvisi.",
            "Conferma disattivazione", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
            ShowError("Errore disattivazione", ex);
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
            ShowError("Errore lettura medicina", ex);
            return;
        }

        using var dialog = new ChangeScheduleDialog(row.Name, currentDose, currentFreq, startDate);
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
            ShowError("Errore cambio schedule", ex);
        }
    }

    private async Task ShowRegisterIntakeAsync()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        // La dose default per il dialog è quella corrente della medicina.
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
            ShowError("Errore lettura medicina", ex);
            return;
        }

        using var dialog = new IntakeDialog(row.Name, row.Unit, suggestedQuantity);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var usecase = scope.ServiceProvider.GetRequiredService<RegisterIntake>();
            await usecase.ExecuteAsync(dialog.Result.ToCommand(row.Id), CancellationToken.None);
            _log.LogInformation("Assunzione registrata per medicina {MedicineId}: {Status} {Quantity} il {Day}",
                row.Id, dialog.Result.Status, dialog.Result.Quantity, dialog.Result.Day);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Errore registrazione assunzione", ex);
        }
    }

    private async Task ShowStockDialogAsync(StockOperationKind defaultKind)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        using var dialog = new StockAdjustmentDialog(row.Name, row.CurrentStock, row.Unit, defaultKind);
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
            ShowError("Errore movimento scorte", ex);
        }
    }

    private async Task RunMonitorAsync()
    {
        SetStatus("Controllo in corso…");
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var catchUp = scope.ServiceProvider.GetRequiredService<ConsumptionCatchUp>();
            var monitor = scope.ServiceProvider.GetRequiredService<MedicationMonitor>();
            await catchUp.RunAsync(CancellationToken.None);
            var result = await monitor.RunAsync(CancellationToken.None);
            SetStatus($"Controllo completato: {result.MedicinesInspected} medicine, {result.NotificationsSent} notifiche inviate.");
            _lastCheckLabel.Text = $"Ultimo controllo: {DateTime.Now:HH:mm}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            ShowError("Errore controllo periodico", ex);
        }
    }

    private void ShowSettings()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using var dialog = new SettingsDialog(
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<SmtpSettings>>(),
                scope.ServiceProvider.GetRequiredService<IOptionsMonitor<BackupSettings>>(),
                scope.ServiceProvider.GetRequiredService<ISmtpCredentialStore>(),
                scope.ServiceProvider.GetRequiredService<IEmailNotificationService>(),
                scope.ServiceProvider.GetRequiredService<IAutoStartService>(),
                scope.ServiceProvider.GetRequiredService<IBackupService>(),
                scope.ServiceProvider.GetRequiredService<IBackupStateStore>(),
                scope.ServiceProvider.GetRequiredService<IApplicationRestarter>(),
                scope.ServiceProvider.GetRequiredService<ILocalizationService>());
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError("Errore apertura impostazioni", ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _log.LogError(ex, "{Title}", title);
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
