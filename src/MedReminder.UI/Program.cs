using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Localization;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Forms;
using MedReminder.UI.Hosting;
using MedReminder.UI.Notifications;
using MedReminder.UI.Presentation;
using MedReminder.UI.Services;
using MedReminder.UI.Tray;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormWindowState = System.Windows.Forms.FormWindowState;
using WinFormUnhandledExceptionMode = System.Windows.Forms.UnhandledExceptionMode;
using WinFormThreadExceptionEventArgs = System.Threading.ThreadExceptionEventArgs;

namespace MedReminder.UI;

// Composition root: costruisce l'IHost, inizializza il DB, avvia lo
// scheduler in background e passa il controllo al message loop WinForms.
internal static class Program
{
    private const string MinimizedArgument = "--minimized";
    // Local\\ prefix: mutex per-utente (sessione Terminal Server), non
    // per-macchina. Un secondo utente Windows sulla stessa macchina può
    // avviare la propria istanza.
    private const string SingleInstanceMutexName = @"Local\MedReminder.SingleInstance.b0000004-4444-4444-4444-444444444444";
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);
    // Attesa massima sull'acquisizione del mutex all'avvio. Il caso
    // tipico è il restart auto dopo restore backup: il nuovo processo
    // parte mentre il vecchio si sta chiudendo (rilascia il mutex nel
    // finally di Main). 5 secondi coprono lo shutdown pulito del monitor
    // hosted service e del pool SQLite senza percepibile ritardo per
    // avvii "normali" (dove il mutex è libero all'istante).
    private static readonly TimeSpan SingleInstanceAcquireTimeout = TimeSpan.FromSeconds(5);

    // ILocalizationService "standalone" per i messaggi pre-IHost
    // (mutex single-instance, ThreadException). Popolato all'inizio
    // di Main leggendo user.settings.json a mano; l'istanza vera del
    // servizio (Singleton via DI) è indipendente ma legge dallo
    // stesso file, quindi il testo è sempre coerente.
    private static ILocalizationService _bootstrapLoc = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Logger = ConfigureSerilog();

        _bootstrapLoc = LocalizationService.CreateStandalone(ReadUserLanguage());

        // Le eccezioni nate dentro handler UI (es. click sul pulsante
        // "Stampa" di PrintPreviewDialog che invoca la SaveAs del driver
        // PDF virtuale — l'annullamento genera Win32Exception 87) NON
        // risalgono ai try/catch delle nostre form: le raccoglie il
        // message loop. Con CatchException le indirizza qui invece di
        // uccidere il processo.
        WinFormsApp.SetUnhandledExceptionMode(WinFormUnhandledExceptionMode.CatchException);
        WinFormsApp.ThreadException += OnUnhandledUiException;

        using var singleInstance = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        bool acquired = false;
        try
        {
            acquired = singleInstance.WaitOne(SingleInstanceAcquireTimeout, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Un'istanza precedente è terminata senza rilasciare il mutex:
            // Windows ce lo restituisce ownership-migrated. Consideriamola
            // acquisita — la vecchia istanza è morta.
            acquired = true;
        }

        if (!acquired)
        {
            Log.Information("Un'altra istanza di MedReminder è già in esecuzione. Uscita.");
            System.Windows.Forms.MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.AlreadyRunning"),
                _bootstrapLoc.Get("Ui.App.AlreadyRunning.Title"),
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
            Log.CloseAndFlush();
            return;
        }

        try
        {
            using var host = BuildHost(args);
            InitializeDatabase(host);
            host.StartAsync().GetAwaiter().GetResult();

            try
            {
                RunUi(host, startMinimized: args.Contains(MinimizedArgument));
            }
            finally
            {
                StopHost(host);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "MedReminder terminato da eccezione non gestita.");
            throw;
        }
        finally
        {
            try { singleInstance.ReleaseMutex(); } catch { /* già rilasciato */ }
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // File utente sovrapposti ad appsettings.json.
        // reloadOnChange=true fa aggiornare gli IOptionsMonitor<T>
        // senza riavvio quando l'utente cambia impostazioni dalla UI.
        var appDataDir = AppDataPaths.GetAppDataDirectory();
        var userSmtpSettingsFile = Path.Combine(appDataDir, "smtp.settings.json");
        var userBackupSettingsFile = Path.Combine(appDataDir, "backup.settings.json");
        // user.settings.json (Incremento 16): al momento contiene solo
        // la lingua dell'UI. reloadOnChange=false perché il cambio
        // richiede comunque restart — la lettura avviene una sola volta
        // al boot del processo.
        var userSettingsFile = Path.Combine(appDataDir, "user.settings.json");

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(userSmtpSettingsFile, optional: true, reloadOnChange: true)
            .AddJsonFile(userBackupSettingsFile, optional: true, reloadOnChange: true)
            .AddJsonFile(userSettingsFile, optional: true, reloadOnChange: false);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddMedReminderApplication();
        builder.Services.AddMedReminderInfrastructure(builder.Configuration);

        // La UI usa i Toast Windows moderni come primary, con fallback
        // sul balloon della tray icon condivisa. Sovrascrive la
        // registrazione default fatta da AddMedReminderInfrastructure
        // (BalloonTipNotificationService).
        builder.Services.RemoveAll<IWindowsNotificationService>();
        builder.Services.AddSingleton<TrayBalloonNotificationService>();
        builder.Services.AddSingleton<IWindowsNotificationService, ToastWindowsNotificationService>();
        builder.Services.AddSingleton<ApplicationTrayIcon>();

        builder.Services.AddScoped<MedicineOverviewLoader>();

        builder.Services.AddHostedService<MedicationMonitorHostedService>();
        builder.Services.AddHostedService<AutomaticBackupHostedService>();

        // Restarter usato dopo un restore del DB (richiede rilancio
        // dell'exe corrente per riacquisire i lock SQLite in modo pulito).
        builder.Services.AddSingleton<IApplicationRestarter, ApplicationRestarter>();

        builder.Services.AddTransient<MainForm>();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);

        return builder.Build();
    }

    private static void InitializeDatabase(IHost host)
    {
        using var scope = host.Services.CreateScope();
        var init = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        init.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void RunUi(IHost host, bool startMinimized)
    {
        var mainForm = host.Services.GetRequiredService<MainForm>();
        if (startMinimized)
        {
            mainForm.WindowState = WinFormWindowState.Minimized;
            mainForm.ShowInTaskbar = false;
        }
        WinFormsApp.Run(mainForm);

        // Disponi esplicitamente la tray icon dopo la chiusura del message
        // loop: senza dispose l'icona rimane visibile in tray fino allo
        // shutdown del processo.
        try
        {
            host.Services.GetRequiredService<ApplicationTrayIcon>().Dispose();
        }
        catch
        {
            // ignoriamo: siamo in shutdown.
        }
    }

    private static void StopHost(IHost host)
    {
        using var cts = new CancellationTokenSource(HostStopTimeout);
        try
        {
            host.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Timeout accettabile.
        }
    }

    private static void OnUnhandledUiException(object? sender, WinFormThreadExceptionEventArgs e)
    {
        var ex = e.Exception;

        if (IsPrintingException(ex))
        {
            // Caso tipico: l'utente annulla la finestra "Salva PDF" del
            // driver Microsoft Print to PDF. Il framework rilancia
            // Win32Exception(87) dal dispositivo di stampa. Non è un
            // errore dell'app — messaggio pulito, log a livello info.
            Log.Information(ex, "Stampa annullata dall'utente o non completata.");
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    _bootstrapLoc.Get("Ui.App.PrintCancelled.Body"),
                    _bootstrapLoc.Get("Ui.App.PrintCancelled.Title"),
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
                // ignoriamo: siamo in un contesto di errore, la MessageBox
                // non deve mai propagare una seconda eccezione.
            }
            return;
        }

        Log.Error(ex, "Eccezione non gestita nel thread UI.");
        try
        {
            System.Windows.Forms.MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Body", ex.Message),
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Title"),
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
        catch
        {
            // ignoriamo: come sopra, evitiamo cascate.
        }
    }

    // Legge %LOCALAPPDATA%\MedReminder\user.settings.json senza
    // dipendere dal binding IOptions/IConfiguration (che non esiste
    // ancora al momento della chiamata). Struttura attesa:
    //   { "UI": { "Language": "en" } }
    // Ricade su null (→ default "en" nel service) se il file manca
    // o è corrotto.
    private static string? ReadUserLanguage()
    {
        try
        {
            var path = Path.Combine(AppDataPaths.GetAppDataDirectory(), "user.settings.json");
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("UI", out var ui) &&
                ui.TryGetProperty("Language", out var lang) &&
                lang.ValueKind == JsonValueKind.String)
            {
                return lang.GetString();
            }
        }
        catch
        {
            // File corrotto o unparsable: silenzioso, ricadiamo su default.
        }
        return null;
    }

    // Riconosce le eccezioni che nascono dallo stack di stampa
    // (System.Drawing.Printing e drivers virtuali PDF/XPS). Cammina
    // la catena InnerException perché il wrapper esterno può essere
    // un TargetInvocationException lanciato dal message loop.
    private static bool IsPrintingException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception)
            {
                // Se il produttore è la pipeline di stampa, lo stack lo mostra.
                var trace = current.StackTrace ?? string.Empty;
                if (trace.Contains("System.Drawing.Printing", StringComparison.Ordinal) ||
                    trace.Contains("PrintDocument", StringComparison.Ordinal) ||
                    trace.Contains("PrintController", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            var typeNs = current.GetType().Namespace ?? string.Empty;
            if (typeNs.StartsWith("System.Drawing.Printing", StringComparison.Ordinal))
            {
                return true;
            }

            var outerTrace = current.StackTrace ?? string.Empty;
            if (outerTrace.Contains("System.Drawing.Printing.", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static Serilog.Core.Logger ConfigureSerilog()
    {
        var logPath = Path.Combine(AppDataPaths.GetLogsDirectory(), "medreminder-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
