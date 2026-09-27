using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class DoseReminderEventConfiguration
    : IEntityTypeConfiguration<DoseReminderEvent>
{
    public void Configure(EntityTypeBuilder<DoseReminderEvent> builder)
    {
        builder.ToTable("DoseReminderEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SlotKey).IsRequired().HasMaxLength(40);

        // LocalDate stored as "yyyy-MM-dd" TEXT. EF Core's DateOnly
        // converter handles the round-trip transparently.
        builder.Property(e => e.LocalDate);

        builder.Property(e => e.Channel).HasConversion<int>();

        // The dedup guarantee: at most one row per (MedicineId, SlotKey,
        // LocalDate). Mirrors CREATE UNIQUE INDEX in DatabaseInitializer.
        builder.HasIndex(e => new { e.MedicineId, e.SlotKey, e.LocalDate })
               .IsUnique();
    }
}
