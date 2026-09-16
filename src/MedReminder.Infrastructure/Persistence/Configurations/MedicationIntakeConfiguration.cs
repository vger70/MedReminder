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

        builder.HasIndex(i => i.MedicineId);
        builder.HasIndex(i => new { i.MedicineId, i.Day });
        builder.HasIndex(i => new { i.MedicineId, i.ScheduledAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(i => i.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
