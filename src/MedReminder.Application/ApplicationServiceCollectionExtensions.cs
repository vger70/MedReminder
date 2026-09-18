using MedReminder.Application.Catalogue;
using MedReminder.Application.Monitoring;
using MedReminder.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.Application;

// DI entrypoint of the Application layer: the UI (Increment 5) calls
// services.AddMedReminderApplication() to register the use cases and
// the monitoring services with Scoped lifetime (one scope per monitor
// tick, one scope per user interaction).
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

        // Reference catalogue (M1). The country-profile provider owns
        // the "national ∪ EU" rule; use cases are cheap façades over
        // the ports registered by the Infrastructure layer.
        services.AddSingleton<ICountryProfileProvider, StaticCountryProfileProvider>();
        services.AddScoped<SearchCatalogueUseCase>();
        services.AddScoped<LinkMedicineToReferenceUseCase>();

        return services;
    }
}
