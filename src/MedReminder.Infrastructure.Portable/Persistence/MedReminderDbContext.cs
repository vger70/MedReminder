using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
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
