using MedReminder.Domain.Medicines;
using MedReminder.Domain.Prescriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Mirrors the CREATE TABLE of the prescription boot patch in
// DatabaseInitializer.
internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("Prescriptions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(PrescriptionRules.MaxCodeLength);

        builder.HasIndex(p => p.MedicineId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(p => p.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Device-local: no foreign key, like DoseReminderEvents; removed with the
// medicine by MedicineDeletionRepository.
internal sealed class PrescriptionReminderEventConfiguration : IEntityTypeConfiguration<PrescriptionReminderEvent>
{
    public void Configure(EntityTypeBuilder<PrescriptionReminderEvent> builder)
    {
        builder.ToTable("PrescriptionReminderEvents");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PrescriptionId, e.ValidUntil }).IsUnique();
    }
}
