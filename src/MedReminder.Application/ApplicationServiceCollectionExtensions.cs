using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Donations;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Timeline;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Sync;
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
        // B.1 Phase 3a: operation capture. Records nothing until sync
        // is enabled for the profile (ISyncSettingsStore).
        services.AddScoped<IOperationLog, OperationLog>();
        services.AddScoped<SyncRegisters>();
        // B.1 Phase 3b: merge of operations from other devices.
        services.AddScoped<ApplyRemoteOperations>();
        // B.1 Phase 3b-2: count re-evaluation by HLC and genesis versions.
        services.AddScoped<CountReevaluation>();
        services.AddScoped<SyncGenesis>();
        // B.1 Phase 3c: the sync engine and the group use cases. The
        // engine takes the transport and key store the host registers
        // (the folder comes from sync.settings.json).
        services.AddScoped<SyncEngine>();
        services.AddScoped<CreateSyncGroup>();
        services.AddScoped<JoinSyncGroup>();
        services.AddScoped<ResetSyncGeneration>();
        // B.1 Phase 3d: the desktop's sync settings window.
        services.AddSingleton<SyncActivity>();
        services.AddScoped<SyncConflictsQuery>();
        services.AddScoped<RestoreSyncConflict>();
        services.AddScoped<DismissSyncConflict>();
        services.AddScoped<DisableSync>();

        services.AddScoped<AddMedicine>();
        services.AddScoped<UpdateMedicine>();
        services.AddScoped<DeactivateMedicine>();
        services.AddScoped<DeleteMedicine>();
        services.AddScoped<AddStock>();
        services.AddScoped<AdjustStockDown>();
        services.AddScoped<ReconcileStock>();
        services.AddScoped<SuspendMedication>();
        services.AddScoped<ResumeMedication>();
        services.AddScoped<ChangeMedicationSchedule>();
        services.AddScoped<RegisterIntake>();

        // B.1 ledger derivation (Phase 2c-2).
        services.AddScoped<LedgerFactsLoader>();
        services.AddScoped<LedgerSynchronizer>();
        services.AddScoped<FactHistoryQuery>();
        services.AddScoped<RetractFact>();

        services.AddScoped<ConsumptionCatchUp>();
        services.AddScoped<MedicationMonitor>();
        services.AddScoped<DoseReminderService>();

        // Therapy timeline view (EVOLUTION-PROPOSALS §4.3): read-only.
        services.AddScoped<TherapyTimelineQuery>();
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
