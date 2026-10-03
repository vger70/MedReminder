using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationAdministrationSlotConfiguration
    : IEntityTypeConfiguration<MedicationAdministrationSlot>
{
    public void Configure(EntityTypeBuilder<MedicationAdministrationSlot> builder)
    {
        builder.ToTable("MedicationAdministrationSlots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Dose).HasConversion<string>();
        // Native TimeOnly in EF Core 8+ (mapped to TEXT on SQLite).
        builder.Property(s => s.Time);
        builder.Property(s => s.TimingLabel).HasMaxLength(200);
        builder.Property(s => s.Order);
        builder.Property(s => s.IsAsNeeded);
        builder.Property(s => s.PresetId);

        builder.HasIndex(s => s.MedicineId);
        builder.HasIndex(s => new { s.MedicineId, s.Order });
        // No FK towards the set: the B.1 boot patch adds SetId with
        // ALTER TABLE, which cannot add a constraint, and fresh and
        // patched databases must have the same shape.
        builder.HasIndex(s => s.SetId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
