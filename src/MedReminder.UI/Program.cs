using System.ComponentModel;
using System.Text.Json;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Notifications;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Localization;
using MedReminder.Infrastructure.Migration;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Settings;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using MedReminder.UI.Forms;
using MedReminder.UI.Hosting;
using MedReminder.UI.Notifications;
using MedReminder.Application.Overview;
using MedReminder.UI.Services;
using MedReminder.UI.Tray;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormThreadExceptionEventArgs = System.Threading.ThreadExceptionEventArgs;
using WinFormUnhandledExceptionMode = System.Windows.Forms.UnhandledExceptionMode;
using WinFormWindowState = System.Windows.Forms.FormWindowState;

namespace MedReminder.UI;

// Composition root: builds the IHost, initializes the DB, starts
// the scheduler in the background and hands control off to the
// WinForms message loop.
//
// Increment 15c (docs/ANALYSIS-MULTI-USER.md §4.1): the boot flow
// now runs the V1 → V2 migrator, reads the profile registry, and
// hands the selected profile to AddMedReminderInfrastructure.
internal static class Program
{
    private const string MinimizedArgument = "--minimized";
    private const string ProfileArgumentPrefix = "--profile";

    // Local\\ prefix: per-user mutex (Terminal Server session),
    // not per-machine. A second Windows user on the same machine can
    // launch their own instance. Increment 15c keeps the mutex
    // per-machine + Windows user, independent of the profile (§10.1).
    private const string SingleInstanceMutexName = @"Local\MedReminder.SingleInstance.b0000004-4444-4444-4444-444444444444";
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SingleInstanceAcquireTimeout = TimeSpan.FromSeconds(5);

    private static ILocalizationService _bootstrapLoc = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Log.Logger = ConfigureSerilog();

        _bootstrapLoc = LocalizationService.CreateStandalone(ReadUserLanguage(), AppDataPaths.GetAppDataDirectory());

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
            acquired = true;
        }

        if (!acquired)
        {
            Log.Information("Another instance of MedReminder is already running. Exiting.");
            MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.AlreadyRunning"),
                _bootstrapLoc.Get("Ui.App.AlreadyRunning.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Log.CloseAndFlush();
            return;
        }

        try
        {
            // ---- Multi-profile boot flow (Increment 15c) ----

            // 1) Run the V1 → V2 migrator if it applies. Idempotent —
            //    subsequent starts short-circuit on the first check.
            RunMigrationIfNeeded();

            // 2) Build the ProfileRegistry directly. AddMedReminderInfrastructure
            //    also creates one internally, but here we need it BEFORE
            //    the DI container is built to decide which profile to open.
            var registry = new ProfileRegistry(
                AppDataPaths.GetProfilesRegistryPath(),
                AppDataPaths.GetProfilesRootDirectory(),
                TimeProvider.System);

            // 3) Decide which profile to open (picker / hint / --profile
            //    / first-run wizard). May exit if the user cancels.
            var startMinimized = args.Contains(MinimizedArgument);
            var explicitProfileId = TryReadProfileArg(args);
            var current = ChooseProfile(registry, explicitProfileId, startMinimized, args);
            if (current is null)
            {
                Log.Information("Boot flow ended without a selected profile. Exiting.");
                return;
            }

            // Update the "last used" hint so the next auto-start
            // (Windows Run entry with --minimized) opens the same
            // profile again (§4.2).
            try { registry.SetActiveProfileHint(current.Id); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update ActiveProfileIdHint for {Profile}.", current.Id);
            }

            // Per-profile text size (EVOLUTION-PROPOSALS.md §3.2). Read
            // here, after the profile is known and before the main
            // window exists; every MedReminderFormBase applies it on load.
            var textSize = ProfileUiSettingsFile.ReadTextSize(current.DataDirectory);
            MedReminderFormBase.TextScale = TextSizes.ScaleOf(textSize);
            Log.Information("Text size for this profile: {TextSize}.", textSize);

            using var host = BuildHost(args, current);
            InitializeDatabase(host);
            AdoptRotatedProfileKey(host, current);
            ReconcileHousehold(host);
            host.StartAsync().GetAwaiter().GetResult();

            try
            {
                RunUi(host, startMinimized: startMinimized);
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

    private static void RunMigrationIfNeeded()
    {
        try
        {
            var appDataRoot = AppDataPaths.GetAppDataDirectory();
            var seedRegistry = new ProfileRegistry(
                AppDataPaths.GetProfilesRegistryPath(),
                AppDataPaths.GetProfilesRootDirectory(),
                TimeProvider.System);
            var migrator = new MigrationV1toV2(
                appDataRoot,
                seedRegistry,
                TimeProvider.System,
                NullLogger<MigrationV1toV2>.Instance);
            var outcome = migrator.Run();
            if (outcome == MigrationOutcome.Migrated)
            {
                Log.Information("V1 → V2 migration completed successfully at boot.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex,
                "V1 → V2 migration failed. The pre-migration backup at %LOCALAPPDATA%\\MedReminder\\backups\\ still contains the original V1 files.");
            MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.MigrationFailed.Body", ex.Message),
                _bootstrapLoc.Get("Ui.App.MigrationFailed.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            throw;
        }
    }

    // Decides the profile to open. Returns null when the user
    // cancels the picker or the wizard — the caller then exits.
    private static ICurrentProfile? ChooseProfile(
        ProfileRegistry registry, string? explicitProfileId, bool startMinimized, string[] args)
    {
        var profiles = registry.ListProfiles();

        // First-run wizard: no profile exists yet (§12.3). Household step
        // H3d-2: or the join of an existing installation, after which the
        // start goes on with the profiles it brought.
        if (profiles.Count == 0)
        {
            ApplySystemLanguageOnFirstRun();
            while (true)
            {
                using var wizard = new FirstRunWizardForm(registry, _bootstrapLoc);
                var result = wizard.ShowDialog();
                if (wizard.JoinRequested)
                {
                    if (RunFirstRunJoin(args) && registry.ListProfiles().Count > 0) break;
                    continue;
                }
                if (result != System.Windows.Forms.DialogResult.OK || wizard.CreatedProfile is null)
                {
                    return null;
                }
                return new CurrentProfile(wizard.CreatedProfile);
            }
            profiles = registry.ListProfiles();
        }

        // --profile <id> from CLI — highest priority (§4.3).
        if (!string.IsNullOrWhiteSpace(explicitProfileId))
        {
            var explicitProfile = registry.GetById(explicitProfileId!);
            if (explicitProfile is not null)
            {
                return VerifyPinIfNeeded(registry, explicitProfile);
            }
            Log.Warning("--profile {Id} did not match any registered profile; falling back to picker.", explicitProfileId);
        }

        // Auto-start (--minimized) uses the hint without a picker (§4.2).
        if (startMinimized)
        {
            var hinted = ResolveHintedProfile(registry, profiles);
            if (hinted is not null)
            {
                return VerifyPinIfNeeded(registry, hinted);
            }
        }

        // Single profile — auto-select (§4.1).
        if (profiles.Count == 1)
        {
            return VerifyPinIfNeeded(registry, profiles[0]);
        }

        // Multiple profiles — picker.
        using var picker = new ProfilePickerForm(registry, _bootstrapLoc);
        var pickerResult = picker.ShowDialog();
        if (pickerResult != System.Windows.Forms.DialogResult.OK || picker.SelectedProfile is null)
        {
            return null;
        }
        return VerifyPinIfNeeded(registry, picker.SelectedProfile);
    }

    private static Profile? ResolveHintedProfile(
        ProfileRegistry registry, IReadOnlyList<Profile> profiles)
    {
        var hint = registry.ActiveProfileIdHint;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var byHint = registry.GetById(hint!);
            if (byHint is not null) return byHint;
        }
        // No hint or stale hint: fall back to the first profile by
        // LastUsedAt so auto-start still lands on the most recently
        // used one.
        return profiles.OrderByDescending(p => p.LastUsedAt).FirstOrDefault();
    }

    private static ICurrentProfile? VerifyPinIfNeeded(ProfileRegistry registry, Profile profile)
    {
        if (!profile.HasPin)
        {
            return new CurrentProfile(profile);
        }
        using var prompt = new PinPromptForm(registry, profile, _bootstrapLoc);
        var result = prompt.ShowDialog();
        if (result != System.Windows.Forms.DialogResult.OK)
        {
            // Cancel or three failed attempts. Either way the app
            // must not open the profile without a valid PIN.
            return null;
        }
        return new CurrentProfile(profile);
    }

    private static string? TryReadProfileArg(string[] args)
    {
        // Supports both "--profile abc123" and "--profile=abc123".
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, ProfileArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length) return args[i + 1];
                return null;
            }
            var prefix = ProfileArgumentPrefix + "=";
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return a[prefix.Length..];
            }
        }
        return null;
    }

    // Household step H3d-2: the join of an existing installation before any
    // profile exists. A host is built for the services the join needs and
    // never started (no hosted service runs); its profile is a placeholder
    // whose database is never opened (SetupProfile). True when the join
    // brought at least one profile.
    private static bool RunFirstRunJoin(string[] args)
    {
        var setup = SetupProfile.Create();
        try
        {
            using var host = BuildHost(args, setup);
            using var scope = host.Services.CreateScope();
            var sp = scope.ServiceProvider;
            using var dialog = new HouseholdDialog(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<HouseholdHostedService>(),
                sp.GetRequiredService<ICloudAccountService>(),
                setup,
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetRequiredService<IApplicationRestarter>(),
                setup: true);
            var joined = dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK;
            Log.Information("First-run join ended; profiles brought: {Joined}.", joined);
            return joined;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "First-run join failed.");
            MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Body", ex.Message),
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            SetupProfile.Remove(setup);
        }
    }

    // The configuration files a profile's services read: the installation
    // settings and the profile's notifications file (§7.1; absent on a
    // fresh install, the settings dialog writes it the first time the user
    // saves a ToAddress). Also used for the profiles the master checks
    // (household step H4c, MasterProfilesHostedService), without reload.
    internal static void AddProfileConfiguration(IConfigurationBuilder configuration, ICurrentProfile profile,
        bool reloadOnChange)
    {
        var appDataDir = AppDataPaths.GetAppDataDirectory();
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(appDataDir, "smtp.settings.json"), optional: true, reloadOnChange: reloadOnChange)
            .AddJsonFile(Path.Combine(appDataDir, "backup.settings.json"), optional: true, reloadOnChange: reloadOnChange)
            .AddJsonFile(Path.Combine(appDataDir, "user.settings.json"), optional: true, reloadOnChange: reloadOnChange)
            .AddJsonFile(profile.NotificationSettingsPath, optional: true, reloadOnChange: reloadOnChange);
    }

    private static IHost BuildHost(string[] args, ICurrentProfile currentProfile)
    {
        var builder = Host.CreateApplicationBuilder(args);

        AddProfileConfiguration(builder.Configuration, currentProfile, reloadOnChange: true);

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddMedReminderApplication();
        builder.Services.AddMedReminderInfrastructure(builder.Configuration, currentProfile);

        // A5: override the Application-layer DoseReminderService
        // registration so the grace window is read from configuration
        // (DoseReminder:GraceWindowMinutes; defaults to 30 minutes).
        // The rest of the dependencies are resolved from DI as usual.
        builder.Services.AddScoped<DoseReminderService>(sp =>
        {
            var minutes = builder.Configuration.GetValue<int?>("DoseReminder:GraceWindowMinutes");
            TimeSpan? graceWindow = minutes is > 0 ? TimeSpan.FromMinutes(minutes.Value) : null;
            return new DoseReminderService(
                sp.GetRequiredService<IMedicineRepository>(),
                sp.GetRequiredService<IStockMovementRepository>(),
                sp.GetRequiredService<IMedicationScheduleHistoryRepository>(),
                sp.GetRequiredService<IMedicationSuspensionRepository>(),
                sp.GetRequiredService<IMedicationAdministrationSlotRepository>(),
                sp.GetRequiredService<IDoseReminderEventRepository>(),
                sp.GetRequiredService<IEmailNotificationService>(),
                sp.GetRequiredService<IWindowsNotificationService>(),
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<DoseReminderService>>(),
                sp.GetService<ILocalizationService>(),
                graceWindow,
                sp.GetService<IMasterRole>());
        });

        builder.Services.RemoveAll<IWindowsNotificationService>();
        builder.Services.AddSingleton<TrayBalloonNotificationService>();
        builder.Services.AddSingleton<IWindowsNotificationService, ToastWindowsNotificationService>();
        builder.Services.AddSingleton<ApplicationTrayIcon>();
        // Webcam barcode scan (A2 phase 2). Singleton: it serializes
        // camera sessions; each session releases the camera on exit.
        builder.Services.AddSingleton<ICameraCaptureService, MedReminder.UI.Camera.WindowsCameraCaptureService>();

        builder.Services.AddScoped<MedicineOverviewLoader>();

        builder.Services.AddHostedService<MedicationMonitorHostedService>();
        builder.Services.AddHostedService<DoseReminderHostedService>();
        builder.Services.AddHostedService<AutomaticBackupHostedService>();

        // B.1 Phase 3d: device sync. The service is a singleton so the
        // sync window can run or suspend it; it stays idle while sync is
        // not enabled for the profile.
        builder.Services.AddSingleton(new MedReminder.Application.Sync.SyncEngineOptions
        {
            DeviceName = Environment.MachineName,
            Platform = "desktop",
            AppVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? string.Empty,
        });
        builder.Services.AddSingleton<MedReminder.UI.Services.SyncStatus>();
        builder.Services.AddSingleton<SyncHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SyncHostedService>());
        // Household step H3d: the household group, idle until the
        // installation is published or joined.
        builder.Services.AddSingleton<HouseholdHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<HouseholdHostedService>());
        // Step H4c: the master checks every profile of this device.
        builder.Services.AddHostedService<MasterProfilesHostedService>();
        // Step H5b: the rotation of a profile's group after a device removal.
        builder.Services.AddScoped<IProfileGroupRotation, ProfileGroupRotation>();

        var catalogueEnabledRaw = builder.Configuration[
            MedReminder.Application.Catalogue.CatalogueFeatureOptions.SectionName + ":Enabled"];
        if (bool.TryParse(catalogueEnabledRaw, out var catalogueEnabled) && catalogueEnabled)
        {
            builder.Services.AddHostedService<CatalogueRefreshHostedService>();
        }

        builder.Services.AddSingleton<IApplicationRestarter, ApplicationRestarter>();

        builder.Services.AddTransient<MainForm>();
        builder.Services.AddTransient<AboutDialog>();
        builder.Services.AddTransient<DonateForm>();

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

    // Household step H5b: before anything uses the open profile's database,
    // a group key rotated after a device removal is taken from the household
    // grant. A failure is logged; the sync window shows the key is needed.
    private static void AdoptRotatedProfileKey(IHost host, ICurrentProfile current)
    {
        try
        {
            ProfileServices.AdoptRotatedKeyAsync(host.Services, current,
                host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MedReminder.Household"),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The new group key of the profile could not be taken from the household.");
        }
    }

    // Household step H2: records in the local household what profiles.json
    // holds and the household does not (the first-run wizard, the V1
    // migration, hand edits). A failure is logged and does not stop the
    // start: the profiles work from profiles.json as before.
    private static void ReconcileHousehold(IHost host)
    {
        try
        {
            using var scope = host.Services.CreateScope();
            var recorded = scope.ServiceProvider.GetRequiredService<MedReminder.Application.UseCases.ReconcileHousehold>()
                .ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (recorded > 0) Log.Information("Household: {Count} profile changes recorded at start.", recorded);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Household reconciliation failed.");
        }
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
            Log.Information(ex, "Print cancelled by the user or not completed.");
            try
            {
                MessageBox.Show(
                    _bootstrapLoc.Get("Ui.App.PrintCancelled.Body"),
                    _bootstrapLoc.Get("Ui.App.PrintCancelled.Title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch
            {
                // ignore
            }
            return;
        }

        Log.Error(ex, "Unhandled exception on the UI thread.");
        try
        {
            MessageBox.Show(
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Body", ex.Message),
                _bootstrapLoc.Get("Ui.App.UnexpectedError.Title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // ignore
        }
    }

    // First run (empty registry, no user.settings.json): adopt the
    // Windows UI language if it is supported (English otherwise) and
    // persist it, so the wizard and the DI-built LocalizationService
    // agree. Existing installs keep their current language because
    // this only runs when no profile exists yet, and an existing
    // user.settings.json is never overwritten.
    private static void ApplySystemLanguageOnFirstRun()
    {
        var path = Path.Combine(AppDataPaths.GetAppDataDirectory(), "user.settings.json");
        if (File.Exists(path))
        {
            return;
        }

        var code = NotificationTexts.DetectSystemLanguageCode();
        try
        {
            var payload = new { UI = new UserSettings { Language = code } };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not persist the first-run UI language.");
        }

        _bootstrapLoc = LocalizationService.CreateStandalone(code, AppDataPaths.GetAppDataDirectory());
        Log.Information("First run: UI language set to {Language} from the system culture.", code);
    }

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
        }
        return null;
    }

    private static bool IsPrintingException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception)
            {
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
