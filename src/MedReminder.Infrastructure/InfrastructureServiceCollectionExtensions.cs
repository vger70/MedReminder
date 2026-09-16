using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.AutoStart;
using MedReminder.Infrastructure.Backup;
using MedReminder.Infrastructure.Credentials;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Notifications;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure;

// Helper di composizione: la UI (Incremento 5) chiamerà
// services.AddMedReminderInfrastructure() per registrare DbContext,
// repository, notifiche, credenziali e auto-start con le lifetime
// corrette.
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMedReminderInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? databasePathOverride = null)
    {
        // ------- Persistenza -------
        var connectionString = AppDataPaths.BuildSqliteConnectionString(databasePathOverride);
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
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<DatabaseInitializer>();

        // ------- Backup automatico -------
        services.Configure<BackupSettings>(configuration.GetSection(BackupSettings.SectionName));
        services.TryAddSingleton<IBackupStateStore, BackupStateStore>();

        // ------- Notifiche + credenziali + auto-start -------
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));

        services.TryAddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.TryAddSingleton<ISmtpCredentialStore, SmtpCredentialStore>();

        // IEmailNotificationService è la MailKit implementation avvolta
        // dal decoratore di retry con back-off (Incremento 7 hardening).
        services.AddSingleton<MailKitEmailNotificationService>();
        services.AddSingleton<IEmailNotificationService>(sp => new RetryingEmailNotificationService(
            sp.GetRequiredService<MailKitEmailNotificationService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RetryingEmailNotificationService>>()));

        // Placeholder Windows notification service: la UI (Incremento 6)
        // sostituirà questa registrazione con quella che riusa la tray icon
        // principale, evitando due icone in tray.
        services.AddSingleton<IWindowsNotificationService, BalloonTipNotificationService>();

        services.AddSingleton<IAutoStartService>(_ => new RegistryAutoStartService());

        return services;
    }
}
