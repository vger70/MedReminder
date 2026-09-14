using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationSuspensionConfiguration
    : IEntityTypeConfiguration<MedicationSuspension>
{
    public void Configure(EntityTypeBuilder<MedicationSuspension> builder)
    {
        builder.ToTable("MedicationSuspensions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Reason).HasMaxLength(500);
        builder.HasIndex(s => s.MedicineId);
        builder.HasIndex(s => new { s.MedicineId, s.EndDate });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
