using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence.Configurations;
using MedReminder.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence;

// EF Core DbContext with the SQLite provider. Entities are the domain
// ones: no DTOs. Fluent configurations live in Configurations/ and
// are applied via ApplyConfigurationsFromAssembly.
public sealed class MedReminderDbContext : DbContext
{
    public MedReminderDbContext(DbContextOptions<MedReminderDbContext> options)
        : base(options)
    {
    }

    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<MedicationScheduleHistory> MedicationScheduleHistories => Set<MedicationScheduleHistory>();
    public DbSet<MedicationSuspension> MedicationSuspensions => Set<MedicationSuspension>();
    public DbSet<MedicationIntake> MedicationIntakes => Set<MedicationIntake>();
    public DbSet<MedicationAdministrationSlot> MedicationAdministrationSlots => Set<MedicationAdministrationSlot>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<DoseReminderEvent> DoseReminderEvents => Set<DoseReminderEvent>();
    public DbSet<SentEmailNotification> SentEmailNotifications => Set<SentEmailNotification>();
    public DbSet<MedicationAdministrationSlotSet> MedicationAdministrationSlotSets => Set<MedicationAdministrationSlotSet>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<LedgerCutoff> LedgerCutoffs => Set<LedgerCutoff>();
    public DbSet<MedicineActivityChange> MedicineActivityChanges => Set<MedicineActivityChange>();
    public DbSet<FactRetraction> FactRetractions => Set<FactRetraction>();
    public DbSet<SyncOperation> SyncOperations => Set<SyncOperation>();
    public DbSet<SyncFieldVersion> SyncFieldVersions => Set<SyncFieldVersion>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<SyncPeer> SyncPeers => Set<SyncPeer>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionReminderEvent> PrescriptionReminderEvents => Set<PrescriptionReminderEvent>();
    public DbSet<Deadline> Deadlines => Set<Deadline>();
    public DbSet<DeadlineReminderEvent> DeadlineReminderEvents => Set<DeadlineReminderEvent>();
    public DbSet<DoseTimePreset> DoseTimePresets => Set<DoseTimePreset>();
    public DbSet<DoseTimeDefault> DoseTimeDefaults => Set<DoseTimeDefault>();
    public DbSet<MedReminder.Domain.Catalogue.ShortageNoticeEvent> ShortageNoticeEvents
        => Set<MedReminder.Domain.Catalogue.ShortageNoticeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new MedicineConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationScheduleHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationSuspensionConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationIntakeConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationAdministrationSlotConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationEventConfiguration());
        modelBuilder.ApplyConfiguration(new DoseReminderEventConfiguration());
        modelBuilder.ApplyConfiguration(new SentEmailNotificationConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationAdministrationSlotSetConfiguration());
        modelBuilder.ApplyConfiguration(new StockCountConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerCutoffConfiguration());
        modelBuilder.ApplyConfiguration(new MedicineActivityChangeConfiguration());
        modelBuilder.ApplyConfiguration(new FactRetractionConfiguration());
        modelBuilder.ApplyConfiguration(new SyncOperationConfiguration());
        modelBuilder.ApplyConfiguration(new SyncFieldVersionConfiguration());
        modelBuilder.ApplyConfiguration(new SyncConflictConfiguration());
        modelBuilder.ApplyConfiguration(new SyncPeerConfiguration());
        modelBuilder.ApplyConfiguration(new PrescriptionConfiguration());
        modelBuilder.ApplyConfiguration(new PrescriptionReminderEventConfiguration());
        modelBuilder.ApplyConfiguration(new DeadlineConfiguration());
        modelBuilder.ApplyConfiguration(new DeadlineReminderEventConfiguration());
        modelBuilder.ApplyConfiguration(new ShortageNoticeEventConfiguration());
        modelBuilder.ApplyConfiguration(new DoseTimePresetConfiguration());
        modelBuilder.ApplyConfiguration(new DoseTimeDefaultConfiguration());

        ApplyDateTimeOffsetConverter(modelBuilder);
    }

    // Bulk-replaces the default DateTimeOffset(TEXT) mapping with
    // INTEGER(long UtcTicks) on the SQLite provider: unlocks ORDER BY
    // and aggregates (Max / Min) on temporal columns. Must be applied
    // AFTER ApplyConfiguration so the iteration sees the full model.
    private static void ApplyDateTimeOffsetConverter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(DateTimeOffsetConverters.ToUtcTicks);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(DateTimeOffsetConverters.ToUtcTicksNullable);
                }
            }
        }
    }
}
