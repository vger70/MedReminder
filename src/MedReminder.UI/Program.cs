using System;
using System.IO;
using System.Linq;
using System.Threading;
using MedReminder.Application;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WinFormsApp = System.Windows.Forms.Application;
using WinForm = System.Windows.Forms.Form;
using WinFormStartPosition = System.Windows.Forms.FormStartPosition;
using WinFormWindowState = System.Windows.Forms.FormWindowState;

namespace MedReminder.UI;

// Composition root: costruisce l'IHost, inizializza il DB, avvia lo
// scheduler in background e passa il controllo al message loop
// WinForms. Il tearing-down è pulito anche in caso di eccezioni
// (StopAsync viene chiamato in finally).
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

        // La factory JSON viene abilitata via Microsoft.Extensions.Configuration.Json.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddMedReminderApplication();
        builder.Services.AddMedReminderInfrastructure(builder.Configuration);

        builder.Services.AddHostedService<MedicationMonitorHostedService>();
        builder.Services.AddTransient<BootstrapForm>();

        // Routing dei log Microsoft.Extensions.Logging -> Serilog.
        // dispose:false: la chiusura di Serilog è controllata dal Main
        // (Log.CloseAndFlush) per garantirla anche in caso di eccezioni.
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
        var mainForm = host.Services.GetRequiredService<BootstrapForm>();
        if (startMinimized)
        {
            mainForm.WindowState = WinFormWindowState.Minimized;
            mainForm.ShowInTaskbar = false;
        }
        WinFormsApp.Run(mainForm);
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
            // Timeout raggiunto: lo shutdown forzato è accettabile.
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

// Placeholder form: sostituito nell'Incremento 6 dalla MainForm reale.
internal sealed class BootstrapForm : WinForm
{
    public BootstrapForm()
    {
        Text = "MedReminder";
        Width = 480;
        Height = 240;
        StartPosition = WinFormStartPosition.CenterScreen;
    }
}
