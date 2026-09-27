using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Localization;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedReminder.Infrastructure;

// Registrations shared by every host (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §4.1): the profile database and its repositories, the archive cipher
// and reader, and the localization service. The host adds its own
// platform adapters, IAppDataLocation included, and the options.
public static class PortableInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMedReminderPortableInfrastructure(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = SqliteConnectionStrings.ForFile(databasePath);
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
        services.AddScoped<IMedicineActivityRepository, MedicineActivityRepository>();
        services.AddScoped<IStockCountRepository, StockCountRepository>();
        services.AddScoped<ILedgerCutoffRepository, LedgerCutoffRepository>();
        services.AddScoped<IFactRetractionRepository, FactRetractionRepository>();
        services.AddScoped<ISyncOperationRepository, SyncOperationRepository>();
        services.AddScoped<ISyncFieldVersionRepository, SyncFieldVersionRepository>();
        services.AddScoped<ISyncConflictRepository, SyncConflictRepository>();
        services.AddScoped<ISyncPeerRepository, SyncPeerRepository>();
        services.AddScoped<ISyncSnapshotStore, SqliteSyncSnapshotStore>();
        // The sync engine's transport: the target of sync.settings.json, a
        // folder (Phase 3c) or a OneDrive account (Phase 4a, with the
        // host's IOneDriveAccessTokens). Resolved only while sync is enabled.
        services.TryAddSingleton<ISyncTransportFactory, SyncTransportFactory>();
        services.TryAddScoped<ISyncTransport>(sp => sp.GetRequiredService<ISyncTransportFactory>().Create(
            SyncTarget.Of(sp.GetRequiredService<ISyncSettingsStore>().Load()
                ?? throw new InvalidOperationException("Sync is not enabled for this profile."))));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DatabaseInitializer>();

        // Per-profile sync state, next to the database (B.1 Phase 3a).
        var profileDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        services.TryAddSingleton<ISyncSettingsStore>(
            new JsonSyncSettingsStore(Path.Combine(profileDirectory, JsonSyncSettingsStore.FileName)));

        // Argon2id + AES-GCM archive cipher and the read half of the
        // import; both stateless.
        services.TryAddSingleton<IArchiveCipher, ArchiveCipher>();
        services.TryAddSingleton<IArchiveReader, ArchiveReader>();

        // Requires IAppDataLocation and IOptions<UserSettings> from the host.
        services.TryAddSingleton<ILocalizationService, LocalizationService>();

        return services;
    }
}
