using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.Application;

// Entrypoint DI dell'Application layer: la UI (Incremento 5) chiama
// services.AddMedReminderApplication() per registrare use case e servizi
// di monitoring con lifetime Scoped (una scope per tick del monitor,
// una scope per interazione utente).
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMedReminderApplication(this IServiceCollection services)
    {
        services.AddScoped<AddMedicine>();
        services.AddScoped<UpdateMedicine>();
        services.AddScoped<AddStock>();
        services.AddScoped<AdjustStockDown>();
        services.AddScoped<SuspendMedication>();
        services.AddScoped<ResumeMedication>();
        services.AddScoped<ChangeMedicationSchedule>();
        services.AddScoped<RegisterIntake>();

        services.AddScoped<ConsumptionCatchUp>();
        services.AddScoped<MedicationMonitor>();

        return services;
    }
}
