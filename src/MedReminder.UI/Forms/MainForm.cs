using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Email;
using MedReminder.UI.Presentation;
using MedReminder.UI.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Forms;

// Finestra principale: elenco medicine, toolbar, integrazione tray.
// Le operazioni asincrone creano una scope DI dedicata via
// IServiceScopeFactory — il DbContext è Scoped, non deve essere
// condiviso tra thread o operazioni concorrenti.
internal sealed class MainForm : Form
{
    private static readonly Color WarningColor = Color.FromArgb(255, 245, 205);
    private static readonly Color EmptyColor = Color.FromArgb(255, 210, 210);
    private static readonly Color SuspendedColor = Color.FromArgb(230, 230, 230);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ApplicationTrayIcon _tray;
    private readonly ILogger<MainForm> _log;

    private DataGridView _grid = null!;
    private BindingList<MedicineListItem> _rows = new();
    private ToolStripButton _btnNew = null!;
    private ToolStripButton _btnEdit = null!;
    private ToolStripButton _btnDeactivate = null!;
    private ToolStripButton _btnAddStock = null!;
    private ToolStripButton _btnAdjustStock = null!;
    private ToolStripButton _btnCheckNow = null!;
    private ToolStripButton _btnSettings = null!;
    private ToolStripButton _btnRefresh = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private Panel _errorBanner = null!;
    private Label _errorBannerLabel = null!;

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
        var toolStrip = BuildToolStrip();
        var statusStrip = BuildStatusStrip();
        _grid = BuildGrid();
        _errorBanner = BuildErrorBanner();

        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // toolbar
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // banner (Visible=false)
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// grid
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
        container.Controls.Add(toolStrip, 0, 0);
        container.Controls.Add(_errorBanner, 0, 1);
        container.Controls.Add(_grid, 0, 2);
        container.Controls.Add(statusStrip, 0, 3);

        Controls.Add(container);
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

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(4),
        };
        _btnNew = MakeButton("Nuova medicina", async () => await ShowNewMedicineAsync());
        _btnEdit = MakeButton("Modifica", async () => await ShowEditMedicineAsync());
        _btnDeactivate = MakeButton("Disattiva", async () => await DeactivateSelectedAsync());
        _btnAddStock = MakeButton("Aggiungi scorte", async () => await ShowStockDialogAsync(StockOperationKind.NewPackage));
        _btnAdjustStock = MakeButton("Correggi scorte", async () => await ShowStockDialogAsync(StockOperationKind.NegativeCorrection));
        _btnCheckNow = MakeButton("Controlla ora", async () => await RunMonitorAsync());
        _btnRefresh = MakeButton("Aggiorna", async () => await ReloadAsync());
        _btnSettings = MakeButton("Impostazioni…", () => { ShowSettings(); return Task.CompletedTask; });

        strip.Items.Add(_btnNew);
        strip.Items.Add(_btnEdit);
        strip.Items.Add(_btnDeactivate);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnAddStock);
        strip.Items.Add(_btnAdjustStock);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnCheckNow);
        strip.Items.Add(_btnRefresh);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_btnSettings);
        return strip;
    }

    private static ToolStripButton MakeButton(string text, Func<Task> action)
    {
        var b = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            AutoSize = true,
        };
        b.Click += async (_, _) => await action();
        return b;
    }

    private StatusStrip BuildStatusStrip()
    {
        _statusLabel = new ToolStripStatusLabel("Pronto.")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var strip = new StatusStrip { SizingGrip = false };
        strip.Items.Add(_statusLabel);
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
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Stato",
            DataPropertyName = nameof(MedicineListItem.StatusDisplay),
            Width = 110,
        });
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
        row.DefaultCellStyle.BackColor = item.Status switch
        {
            MedicineRowStatus.Warning => WarningColor,
            MedicineRowStatus.Empty => EmptyColor,
            MedicineRowStatus.Suspended => SuspendedColor,
            _ => SystemColors.Window,
        };
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
            var medicine = await repo.GetAsync(row.Id, CancellationToken.None);
            if (medicine is null) return;
            seed = new MedicineEditResult(
                medicine.Name, medicine.ActiveIngredient, medicine.Package, medicine.Unit,
                medicine.DosePerAdministration, medicine.AdministrationsPerDay,
                medicine.StartDate, medicine.EndDate, medicine.ThresholdDays,
                medicine.DoctorName, medicine.Notes,
                InitialQuantity: 0m, medicine.NotificationChannels, medicine.IsActive);
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
                scope.ServiceProvider.GetRequiredService<ISmtpCredentialStore>(),
                scope.ServiceProvider.GetRequiredService<IEmailNotificationService>(),
                scope.ServiceProvider.GetRequiredService<IAutoStartService>(),
                scope.ServiceProvider.GetRequiredService<IBackupService>());
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
