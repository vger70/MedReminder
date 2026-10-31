using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationIntakeConfiguration : IEntityTypeConfiguration<MedicationIntake>
{
    public void Configure(EntityTypeBuilder<MedicationIntake> builder)
    {
        builder.ToTable("MedicationIntakes");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).HasConversion<string>();
        builder.Property(i => i.Status).HasConversion<int>();
        builder.Property(i => i.Notes).HasMaxLength(500);
        builder.Property(i => i.IsExtra);
        // Native TimeOnly (TEXT on SQLite), as the slot times.
        builder.Property(i => i.SlotTime);
        // B.1 Phase 2c-2: same default as the boot patch column (0 ticks,
        // before any freeze).
        builder.Property(i => i.RecordedAt).HasDefaultValue(DateTimeOffset.MinValue);

        // (MedicineId, Day) also serves the MedicineId-only reads and the
        // foreign key: a separate (MedicineId) index was dropped (#328).
        builder.HasIndex(i => new { i.MedicineId, i.Day });
        builder.HasIndex(i => new { i.MedicineId, i.ScheduledAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(i => i.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
