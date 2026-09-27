using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationAdministrationSlotSetConfiguration
    : IEntityTypeConfiguration<MedicationAdministrationSlotSet>
{
    public void Configure(EntityTypeBuilder<MedicationAdministrationSlotSet> builder)
    {
        builder.ToTable("MedicationAdministrationSlotSets");
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.MedicineId, s.RecordedAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
