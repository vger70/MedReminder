using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence.Configurations;
using MedReminder.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence;

// DbContext EF Core con provider SQLite. Le entità sono quelle del
// dominio: nessun DTO. Le configurazioni Fluent vivono in
// Configurations/ e vengono applicate via ApplyConfigurationsFromAssembly.
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
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new MedicineConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationScheduleHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationSuspensionConfiguration());
        modelBuilder.ApplyConfiguration(new MedicationIntakeConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationEventConfiguration());

        ApplyDateTimeOffsetConverter(modelBuilder);
    }

    // Sostituisce di massa il mapping default DateTimeOffset(TEXT) →
    // INTEGER(long UtcTicks) sul provider SQLite: sblocca ORDER BY e le
    // aggregate (Max/Min) sui campi temporali. Da applicare DOPO
    // ApplyConfiguration così l'iterazione vede il modello completo.
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
