using MedReminder.Application.Catalogue;
using MedReminder.Application.Donations;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Prescriptions;
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
        services.AddScoped<DoseReminderService>();

        // Prescription request (EVOLUTION-PROPOSALS §3.4): interactive
        // send only, resolved per dialog action.
        services.AddScoped<SendPrescriptionRequest>();

        // Reference catalogue (M1). The country-profile provider owns
        // the "national ∪ EU" rule; use cases are cheap façades over
        // the ports registered by the Infrastructure layer.
        services.AddSingleton<ICountryProfileProvider, StaticCountryProfileProvider>();
        services.AddScoped<SearchCatalogueUseCase>();
        services.AddScoped<LinkMedicineToReferenceUseCase>();

        // Barcode scan (A2). Stateless and pure: one instance for the
        // whole app. The "Capture" options instance is registered by
        // the Infrastructure layer; defaults apply when it is absent.
        services.AddSingleton<IBarcodeParser>(sp =>
            BarcodeParser.FromOptions(sp.GetService<BarcodeCaptureOptions>()));

        // Donation / "Support Development" (A6). The orchestrator is a
        // stateless singleton; the provider adapters, the URL launcher
        // and the bound DonationOptions are registered by the
        // Infrastructure layer (composition root wires the options
        // instance from donations.settings.json).
        services.AddSingleton<DonationService>();

        return services;
    }
}
