using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Backup;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.Infrastructure;

// Helper di composizione: la UI (Incremento 5) chiamerà
// services.AddMedReminderInfrastructure() per registrare DbContext,
// repository e servizi ausiliari con le lifetime corrette.
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMedReminderInfrastructure(
        this IServiceCollection services,
        string? databasePathOverride = null)
    {
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
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
