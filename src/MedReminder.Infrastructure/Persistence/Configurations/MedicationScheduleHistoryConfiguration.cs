using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationScheduleHistoryConfiguration
    : IEntityTypeConfiguration<MedicationScheduleHistory>
{
    public void Configure(EntityTypeBuilder<MedicationScheduleHistory> builder)
    {
        builder.ToTable("MedicationScheduleHistories");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DosePerAdministration).HasConversion<string>();
        builder.HasIndex(s => new { s.MedicineId, s.EffectiveFrom });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
