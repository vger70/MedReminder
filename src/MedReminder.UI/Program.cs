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

// Composition root: builds the IHost, initializes the DB, starts
// the scheduler in the background and hands control off to the
// WinForms message loop.
internal static class Program
{
    private const string MinimizedArgument = "--minimized";
    // Local\\ prefix: per-user mutex (Terminal Server session),
    // not per-machine. A second Windows user on the same machine can
    // launch their own instance.
    private const string SingleInstanceMutexName = @"Local\MedReminder.SingleInstance.b0000004-4444-4444-4444-444444444444";
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);
    // Maximum wait on mutex acquisition at startup. Typical case:
    // the automatic restart after a backup restore — the new process
    // starts while the old one is closing (releases the mutex in
    // Main's finally). 5 seconds cover the clean shutdown of the
    // monitor hosted service and the SQLite pool without noticeable
    // delay for "normal" starts (where the mutex is free
    // instantly).
    private static readonly TimeSpan SingleInstanceAcquireTimeout = TimeSpan.FromSeconds(5);

    // "Standalone" ILocalizationService for the pre-IHost messages
    // (single-instance mutex, ThreadException). Populated at the
    // start of Main by reading user.settings.json by hand; the real
    // service instance (Singleton via DI) is independent but reads
    // the same file, so the text is always consistent.
    private static ILocalizationService _bootstrapLoc = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Logger = ConfigureSerilog();

        _bootstrapLoc = LocalizationService.CreateStandalone(ReadUserLanguage());

        // Exceptions raised inside UI handlers (e.g. clicking the
        // "Print" button of PrintPreviewDialog which invokes the
        // virtual PDF driver's SaveAs — cancellation raises
        // Win32Exception 87) do NOT bubble to our forms' try/catch:
        // the message loop catches them. With CatchException we
        // route them here instead of killing the process.
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
            // A previous instance terminated without releasing the
            // mutex: Windows hands it to us with ownership migrated.
            // Treat it as acquired — the old instance is dead.
            acquired = true;
        }

        if (!acquired)
        {
            Log.Information("Another instance of MedReminder is already running. Exiting.");
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
            Log.Fatal(ex, "MedReminder terminated by an unhandled exception.");
            throw;
        }
        finally
        {
            try { singleInstance.ReleaseMutex(); } catch { /* already released */ }
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // User files layered on top of appsettings.json.
        // reloadOnChange=true refreshes IOptionsMonitor<T> without a
        // restart when the user changes settings from the UI.
        var appDataDir = AppDataPaths.GetAppDataDirectory();
        var userSmtpSettingsFile = Path.Combine(appDataDir, "smtp.settings.json");
        var userBackupSettingsFile = Path.Combine(appDataDir, "backup.settings.json");
        // user.settings.json holds the UI language (still requires a
        // restart to rebind the singleton LocalizationService) and
        // the reference-catalogue country (M2: picked up at the next
        // medicine-form open via IOptionsMonitor<UserSettings>).
        // reloadOnChange=true so the country change takes effect
        // without asking the user to restart.
        var userSettingsFile = Path.Combine(appDataDir, "user.settings.json");

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(userSmtpSettingsFile, optional: true, reloadOnChange: true)
            .AddJsonFile(userBackupSettingsFile, optional: true, reloadOnChange: true)
            .AddJsonFile(userSettingsFile, optional: true, reloadOnChange: true);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddMedReminderApplication();

        // Increment 15a (docs/ANALYSIS-MULTI-USER.md §2.5) removed
        // the implicit default from AppDataPaths — every caller now
        // states which database file it opens. The multi-profile
        // boot flow lands in 15c; until then the composition root
        // keeps the historical single-user location so upgrades run
        // seamlessly. The V1→V2 migrator (15b) moves this file into
        // profiles\default\, at which point Program.cs will pass
        // ICurrentProfile.DatabasePath instead.
        var legacyDatabasePath = Path.Combine(
            appDataDir, AppDataPaths.DatabaseFileName);
        builder.Services.AddMedReminderInfrastructure(
            builder.Configuration, legacyDatabasePath);

        // The UI uses modern Windows toasts as the primary, with a
        // fallback to the shared tray icon's balloon. Overrides the
        // default registration performed by
        // AddMedReminderInfrastructure (BalloonTipNotificationService).
        builder.Services.RemoveAll<IWindowsNotificationService>();
        builder.Services.AddSingleton<TrayBalloonNotificationService>();
        builder.Services.AddSingleton<IWindowsNotificationService, ToastWindowsNotificationService>();
        builder.Services.AddSingleton<ApplicationTrayIcon>();

        builder.Services.AddScoped<MedicineOverviewLoader>();

        builder.Services.AddHostedService<MedicationMonitorHostedService>();
        builder.Services.AddHostedService<AutomaticBackupHostedService>();

        // Reference catalogue (M2): boot-time importer runs only when
        // the feature flag is on. Reads Catalogue:Enabled via the
        // string indexer to avoid an explicit dependency on
        // Microsoft.Extensions.Configuration.Binder — same pattern as
        // MedicationMonitorHostedService. The service itself re-checks
        // the flag before doing anything, so toggling it at runtime
        // stays safe.
        var catalogueEnabledRaw = builder.Configuration[
            MedReminder.Application.Catalogue.CatalogueFeatureOptions.SectionName + ":Enabled"];
        if (bool.TryParse(catalogueEnabledRaw, out var catalogueEnabled) && catalogueEnabled)
        {
            builder.Services.AddHostedService<CatalogueRefreshHostedService>();
        }

        // Restarter used after a DB restore (requires relaunching
        // the current exe to cleanly reacquire the SQLite locks).
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

        // Explicitly dispose the tray icon after the message loop
        // closes: without dispose the icon stays visible in the tray
        // until the process shuts down.
        try
        {
            host.Services.GetRequiredService<ApplicationTrayIcon>().Dispose();
        }
        catch
        {
            // ignore: we are in shutdown.
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
            // Acceptable timeout.
        }
    }

    private static void OnUnhandledUiException(object? sender, WinFormThreadExceptionEventArgs e)
    {
        var ex = e.Exception;

        if (IsPrintingException(ex))
        {
            // Typical case: the user cancels the "Save PDF" window
            // of the Microsoft Print to PDF driver. The framework
            // rethrows Win32Exception(87) from the print device. Not
            // an app error — clean message, info-level log.
            Log.Information(ex, "Print cancelled by the user or not completed.");
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
                // ignore: we are in an error context, the MessageBox
                // must never propagate a second exception.
            }
            return;
        }

        Log.Error(ex, "Unhandled exception on the UI thread.");
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
            // ignore: as above, avoid cascades.
        }
    }

    // Reads %LOCALAPPDATA%\MedReminder\user.settings.json without
    // depending on IOptions / IConfiguration binding (it does not
    // exist yet at the moment of the call). Expected shape:
    //   { "UI": { "Language": "en" } }
    // Falls back to null (→ default "en" in the service) if the file
    // is missing or corrupted.
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
            // Corrupted or unparsable file: silent, fall back to the default.
        }
        return null;
    }

    // Recognizes exceptions raised from the printing stack
    // (System.Drawing.Printing and virtual PDF / XPS drivers). Walks
    // the InnerException chain because the outer wrapper may be a
    // TargetInvocationException thrown by the message loop.
    private static bool IsPrintingException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception)
            {
                // If the producer is the printing pipeline, the stack shows it.
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
