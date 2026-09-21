using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Infrastructure.AutoStart;
using MedReminder.Infrastructure.Backup;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using MedReminder.Infrastructure.Credentials;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Localization;
using MedReminder.Infrastructure.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Storage;
using MedReminder.Infrastructure.UpdateChecking;
using MedReminder.Application.UpdateChecking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure;

// Composition helper: the UI (Increment 5) calls
// services.AddMedReminderInfrastructure() to register the DbContext,
// repositories, notifications, credentials and auto-start with the
// correct lifetimes.
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMedReminderInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        ICurrentProfile currentProfile)
    {
        ArgumentNullException.ThrowIfNull(currentProfile);

        // ------- Multi-profile (Increment 15c) -------
        // The composition root picked the profile up-front (picker,
        // first-run wizard, hint or --profile) and hands us the
        // resolved ICurrentProfile. Register it as a singleton so
        // downstream services (BackupService, MailKit, UI) share
        // exactly the same instance.
        services.TryAddSingleton(currentProfile);

        // ------- Profile registry -------
        // Wired here so the multi-profile-aware UI and the automatic
        // backup service can enumerate every profile without having
        // to reach into Infrastructure.Profiles from Program.cs.
        services.TryAddSingleton<IProfileRegistry>(_ => new ProfileRegistry(
            AppDataPaths.GetProfilesRegistryPath(),
            AppDataPaths.GetProfilesRootDirectory(),
            TimeProvider.System));

        // ------- Persistence -------
        // Increment 15a (docs/ANALYSIS-MULTI-USER.md §2.5): the DB
        // path is now an explicit input. BackupService reads the
        // singleton registered here so both the EF Core connection
        // and the backup export target the same file. In 15c the
        // path comes from ICurrentProfile.
        var connectionString = AppDataPaths.BuildSqliteConnectionString(currentProfile.DatabasePath);
        services.TryAddSingleton(new DatabasePathProvider(currentProfile.DatabasePath));
        services.AddDbContext<MedReminderDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        services.AddScoped<IMedicineRepository, MedicineRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IMedicationScheduleHistoryRepository, MedicationScheduleHistoryRepository>();
        services.AddScoped<IMedicationSuspensionRepository, MedicationSuspensionRepository>();
        services.AddScoped<INotificationEventRepository, NotificationEventRepository>();
        services.AddScoped<IMedicationIntakeRepository, MedicationIntakeRepository>();
        services.AddScoped<IMedicationAdministrationSlotRepository, MedicationAdministrationSlotRepository>();
        services.AddScoped<IDoseReminderEventRepository, DoseReminderEventRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<DatabaseInitializer>();

        // ------- Automatic backup -------
        services.Configure<BackupSettings>(configuration.GetSection(BackupSettings.SectionName));
        services.TryAddSingleton<IBackupStateStore, BackupStateStore>();

        // ------- Localization (Increment 16) -------
        services.Configure<UserSettings>(configuration.GetSection(UserSettings.SectionName));
        services.TryAddSingleton<ILocalizationService, LocalizationService>();

        // ------- Notifications + credentials + auto-start -------
        // Global SMTP transport (admin-managed, §7.1).
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        // Per-profile recipient (§7.1). Loaded from
        // <DataDirectory>\notifications.settings.json — Program.cs
        // adds the file to the configuration chain before calling
        // this extension.
        services.Configure<NotificationSettings>(
            configuration.GetSection(NotificationSettings.SectionName));

        services.TryAddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.TryAddSingleton<ISmtpCredentialStore, SmtpCredentialStore>();

        // IEmailNotificationService is the MailKit implementation
        // wrapped by the retry-with-back-off decorator (Increment 7
        // hardening).
        services.AddSingleton<MailKitEmailNotificationService>();
        services.AddSingleton<IEmailNotificationService>(sp => new RetryingEmailNotificationService(
            sp.GetRequiredService<MailKitEmailNotificationService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RetryingEmailNotificationService>>()));

        // Placeholder Windows notification service: the UI
        // (Increment 6) replaces this registration with one that
        // reuses the main tray icon, avoiding two tray icons.
        services.AddSingleton<IWindowsNotificationService, BalloonTipNotificationService>();

        services.AddSingleton<IAutoStartService>(_ => new RegistryAutoStartService());

        // ------- Passive update check (GitHub Releases) -------
        // Singleton so the underlying HttpClient is reused across
        // both the startup check and the manual "Check for updates
        // now" menu entry.
        services.TryAddSingleton<IUpdateChecker, GitHubUpdateChecker>();

        // ------- Reference catalogue (M1) -------
        //
        // Registered unconditionally so the ports resolve, but the
        // feature stays dormant until M2 wires the UI. The feature
        // flag defaults to false; nothing in M1 reads it, so this
        // registration has no runtime effect.
        services.Configure<CatalogueFeatureOptions>(
            configuration.GetSection(CatalogueFeatureOptions.SectionName));
        services.AddSingleton<EmbeddedSnapshotProvider>();
        services.AddScoped<IReferenceSnapshotParser, AifaSnapshotParser>();
        services.AddScoped<IReferenceSnapshotParser, EmaEparParser>();
        services.AddScoped<IReferenceSnapshotParser, AempsCimaParser>();
        services.AddScoped<IReferenceSnapshotParser, AnsmBdpmParser>();
        services.AddScoped<IReferenceCatalogueQueryService, SqliteReferenceCatalogueQueryService>();
        services.AddScoped<IReferenceCatalogueImporter>(sp => new CsvReferenceCatalogueImporter(
            sp.GetRequiredService<MedReminderDbContext>(),
            sp.GetServices<IReferenceSnapshotParser>(),
            sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
