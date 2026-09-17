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

        builder.HasIndex(s => s.MedicineId);
        builder.HasIndex(s => new { s.MedicineId, s.Order });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
