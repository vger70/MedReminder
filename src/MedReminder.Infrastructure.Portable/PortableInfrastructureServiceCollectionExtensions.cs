using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Catalogue;
using MedReminder.Infrastructure.Catalogue.Parsers;
using MedReminder.Infrastructure.Cloud.GoogleDrive;
using MedReminder.Infrastructure.Cloud.OneDrive;
using MedReminder.Infrastructure.Credentials;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Household;
using MedReminder.Infrastructure.Localization;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Profiles;
using MedReminder.Infrastructure.Settings;
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
        services.AddScoped<IPendingDataMigrations, PendingDataMigrations>();
        services.AddScoped<IDoseTimePresetRepository, DoseTimePresetRepository>();
        services.AddScoped<IMedicineDeletionRepository, MedicineDeletionRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IMedicationScheduleHistoryRepository, MedicationScheduleHistoryRepository>();
        services.AddScoped<IMedicationSuspensionRepository, MedicationSuspensionRepository>();
        services.AddScoped<INotificationEventRepository, NotificationEventRepository>();
        services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
        services.AddScoped<IPrescriptionDispensationRepository, PrescriptionDispensationRepository>();
        services.AddScoped<IPrescriptionReminderEventRepository, PrescriptionReminderEventRepository>();
        services.AddScoped<IDeadlineRepository, DeadlineRepository>();
        services.AddScoped<IDeadlineReminderEventRepository, DeadlineReminderEventRepository>();
        services.AddScoped<IStockPackageRepository, StockPackageRepository>();
        services.AddScoped<IPackageExpiryNoticeEventRepository, PackageExpiryNoticeEventRepository>();
        services.AddScoped<IShortageNoticeEventRepository, ShortageNoticeEventRepository>();
        // Reference catalogue (ANALYSIS-DRUG-CATALOGUE.md §2.3; portable
        // since Android backlog B4-02): one parser per feed, the importer
        // and the query service over the open profile's database, and the
        // remote feed client (the host binds CatalogueFeedOptions).
        // RemoteCatalogueRefresher comes from AddMedReminderApplication.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReferenceSnapshotParser, AifaSnapshotParser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReferenceSnapshotParser, EmaEparParser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReferenceSnapshotParser, AempsCimaParser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReferenceSnapshotParser, AnsmBdpmParser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IReferenceSnapshotParser, OpenFdaNdcParser>());
        services.TryAddScoped<IReferenceCatalogueQueryService, SqliteReferenceCatalogueQueryService>();
        services.TryAddScoped<IReferenceCatalogueImporter>(sp => new CsvReferenceCatalogueImporter(
            sp.GetRequiredService<MedReminderDbContext>(),
            sp.GetServices<IReferenceSnapshotParser>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IDatabaseExclusiveAccess>()));
        services.TryAddSingleton<ICatalogueFeedClient, GitHubRawCatalogueFeedClient>();
        // Shortage list (EVOLUTION-PROPOSALS-2 §3.3): one file for every
        // profile, so the store is a singleton like the client.
        services.AddOptions();
        services.TryAddSingleton<IShortageFeedClient, GitHubRawShortageFeedClient>();
        services.TryAddSingleton<IShortageListStore, JsonFileShortageListStore>();
        // Equivalents list (ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK §2.4):
        // shared by every profile, as the shortage list.
        services.TryAddSingleton<IEquivalenceFeedClient, GitHubRawEquivalenceFeedClient>();
        services.TryAddSingleton<IEquivalenceListStore, JsonFileEquivalenceListStore>();
        // Regional prescription services (PROMPT-REGIONAL-PRESCRIPTION-
        // SERVICES §3.1): shared by every profile, with a shipped copy.
        services.TryAddSingleton<IRegionalServicesFeedClient, GitHubRawRegionalServicesFeedClient>();
        services.TryAddSingleton<IRegionalServicesListStore, JsonFileRegionalServicesListStore>();
        services.AddScoped<ISentEmailNotificationRepository, SentEmailNotificationRepository>();
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
        // One OneDrive client per account, shared with the cloud backup.
        services.TryAddSingleton(sp => new OneDriveClientFactory(
            sp.GetService<IOneDriveAccessTokens>(), clock: sp.GetService<TimeProvider>()));
        // Phase 4b: the same for Google Drive (IGoogleDriveAccessTokens).
        services.TryAddSingleton(sp => new GoogleDriveClientFactory(
            sp.GetService<IGoogleDriveAccessTokens>(), clock: sp.GetService<TimeProvider>()));
        services.TryAddSingleton<ISyncTransportFactory, SyncTransportFactory>();
        services.TryAddScoped<ISyncTransport>(sp => sp.GetRequiredService<ISyncTransportFactory>().Create(
            SyncTarget.Of(sp.GetRequiredService<ISyncSettingsStore>().Load()
                ?? throw new InvalidOperationException("Sync is not enabled for this profile."))));
        // Rebuild of the open profile from its group (B.1 Phase 3d), shared
        // by the desktop and the phone.
        services.TryAddScoped(sp => new ProfileSyncRebuild(databasePath, sp.GetRequiredService<MedReminderDbContext>(),
            sp.GetRequiredService<MedReminder.Application.Sync.JoinSyncGroup>(), sp.GetRequiredService<ISyncSettingsStore>(),
            sp.GetRequiredService<ISyncKeyStore>(), sp.GetRequiredService<ISyncTransportFactory>(),
            sp.GetRequiredService<IDatabaseExclusiveAccess>(), sp.GetRequiredService<TimeProvider>()));
        // The open profile replaced with its group in the installation, after
        // a join that found it kept here from before (step H3d).
        services.TryAddScoped(sp => new HouseholdProfileReplace(databasePath,
            sp.GetRequiredService<ICurrentProfile>().Id, sp.GetRequiredService<MedReminderDbContext>(),
            sp.GetRequiredService<MedReminder.Application.Household.HouseholdKeyring>(),
            sp.GetRequiredService<MedReminder.Application.Household.HouseholdLog>(), sp.GetRequiredService<IHouseholdStore>(),
            sp.GetRequiredService<IProfileGroupKeys>(), sp.GetRequiredService<MedReminder.Application.Sync.JoinSyncGroup>(),
            sp.GetRequiredService<ISyncSettingsStore>(), sp.GetRequiredService<ISyncKeyStore>(),
            sp.GetRequiredService<ISyncTransportFactory>(), sp.GetRequiredService<IDatabaseExclusiveAccess>(),
            sp.GetRequiredService<TimeProvider>()));
        // The new group key after a rotation elsewhere (Phase 4c, step H5b).
        services.TryAddScoped(sp => new ProfileSyncRekey(databasePath, sp.GetRequiredService<MedReminderDbContext>(),
            sp.GetRequiredService<MedReminder.Application.Sync.JoinSyncGroup>(), sp.GetRequiredService<ISyncSettingsStore>(),
            sp.GetRequiredService<ISyncKeyStore>(), sp.GetRequiredService<ISyncTransportFactory>(),
            sp.GetRequiredService<ISyncOperationRepository>(),
            sp.GetRequiredService<MedReminder.Application.Sync.ApplyRemoteOperations>(),
            sp.GetRequiredService<IDatabaseExclusiveAccess>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITransactionalScope, TransactionalScope>();
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

        // Household feature, step H2: one store per installation, next to
        // the profiles. Requires IAppDataLocation from the host.
        services.TryAddSingleton<IHouseholdStore>(sp => new SqliteHouseholdStore(
            Path.Combine(sp.GetRequiredService<IAppDataLocation>().DataDirectory, "household")));
        // Step H3a: the household key, protected by the host's credential
        // protector (DPAPI on Windows).
        services.TryAddSingleton<IHouseholdKeyStore>(sp => new ProtectedHouseholdKeyStore(
            sp.GetRequiredService<ICredentialProtector>(),
            Path.Combine(sp.GetRequiredService<IAppDataLocation>().DataDirectory, "household")));
        // Step H3b: this device's key pair for the household.
        services.TryAddSingleton<IDeviceKeyStore>(sp => new ProtectedDeviceKeyStore(
            sp.GetRequiredService<ICredentialProtector>(),
            Path.Combine(sp.GetRequiredService<IAppDataLocation>().DataDirectory, "household")));

        return services;
    }

    // Android plan M0 (B0-03): the profile and installation settings
    // stores (B0-01), which stay internal. profilesRootDirectory holds
    // one folder per profile (AppDataPaths.GetProfilesRootDirectory() on
    // Windows). The host registers IProfileRegistry, ICurrentProfile,
    // IAppDataLocation and the SmtpSettings, BackupSettings and
    // UserSettings options.
    public static IServiceCollection AddMedReminderPortableProfileSettings(
        this IServiceCollection services,
        string profilesRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);

        services.TryAddSingleton<IProfileSettingsStore, ProfileSettingsStore>();
        services.TryAddSingleton<ISyncProfileStatus>(_ => new SyncProfileStatus(profilesRootDirectory));
        services.TryAddSingleton<IInstallationSettingsStore, InstallationSettingsStore>();
        return services;
    }

    // Android backlog B5-01: the SMTP password behind the host's credential
    // protector, in smtp.protected of the installation's data directory,
    // and the connection tester for the SMTP form. The Windows host
    // registers the same store at AppDataPaths.GetCredentialsPath().
    // Requires ICredentialProtector from the host. The sender itself
    // (MailKitEmailNotificationService behind
    // RetryingEmailNotificationService) is registered by the host that
    // sends.
    public static IServiceCollection AddMedReminderPortableSmtpSetup(
        this IServiceCollection services,
        string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        services.TryAddSingleton<ISmtpCredentialStore>(sp => SmtpCredentialStore.In(
            sp.GetRequiredService<ICredentialProtector>(), dataDirectory));
        services.TryAddSingleton<SmtpConnectionTester>();
        return services;
    }

    // Android plan M2 (B2-05): the sync group keys behind the host's
    // credential protector (ProtectedSyncKeyStore), for a host without its
    // own key store: the open profile's key, the key of any profile for
    // household grants, and the installer of profiles an installation join
    // brings. The Windows host registers its DPAPI stores instead and does
    // not call this. Requires ICredentialProtector from the host.
    public static IServiceCollection AddMedReminderPortableSyncKeys(
        this IServiceCollection services,
        string profilesRootDirectory,
        string currentProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilesRootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentProfileDirectory);

        services.TryAddSingleton<ISyncKeyStore>(sp => ProtectedSyncKeyStore.In(
            sp.GetRequiredService<ICredentialProtector>(), currentProfileDirectory));
        services.TryAddSingleton<IProfileGroupKeys>(sp =>
        {
            var protector = sp.GetRequiredService<ICredentialProtector>();
            return new ProfileGroupKeys(profilesRootDirectory, directory => ProtectedSyncKeyStore.In(protector, directory));
        });
        services.TryAddSingleton<IHouseholdProfileInstaller>(sp =>
        {
            var protector = sp.GetRequiredService<ICredentialProtector>();
            return new HouseholdProfileInstaller(profilesRootDirectory, directory => ProtectedSyncKeyStore.In(protector, directory));
        });
        return services;
    }
}
