using System;
using System.IO;
using System.Linq;
using System.Threading;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Forms;
using MedReminder.UI.Hosting;
using MedReminder.UI.Notifications;
using MedReminder.UI.Presentation;
using MedReminder.UI.Tray;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormWindowState = System.Windows.Forms.FormWindowState;

namespace MedReminder.UI;

// Composition root: costruisce l'IHost, inizializza il DB, avvia lo
// scheduler in background e passa il controllo al message loop WinForms.
internal static class Program
{
    private const string MinimizedArgument = "--minimized";
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Logger = ConfigureSerilog();

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
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Il file utente smtp.settings.json (creato dal SettingsDialog)
        // sovrascrive appsettings.json. reloadOnChange=true fa aggiornare
        // IOptionsMonitor<SmtpSettings> senza riavvio.
        var userSmtpSettingsFile = Path.Combine(
            AppDataPaths.GetAppDataDirectory(), "smtp.settings.json");

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(userSmtpSettingsFile, optional: true, reloadOnChange: true);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddMedReminderApplication();
        builder.Services.AddMedReminderInfrastructure(builder.Configuration);

        // La UI riusa la NotifyIcon principale per emettere le notifiche
        // toast/balloon: sovrascrive la registrazione default fatta da
        // AddMedReminderInfrastructure (BalloonTipNotificationService).
        builder.Services.RemoveAll<IWindowsNotificationService>();
        builder.Services.AddSingleton<IWindowsNotificationService, TrayBalloonNotificationService>();
        builder.Services.AddSingleton<ApplicationTrayIcon>();

        builder.Services.AddScoped<MedicineOverviewLoader>();

        builder.Services.AddHostedService<MedicationMonitorHostedService>();
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
