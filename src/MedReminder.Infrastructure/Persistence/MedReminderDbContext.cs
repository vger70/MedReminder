using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence.Configurations;
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
    }
}
