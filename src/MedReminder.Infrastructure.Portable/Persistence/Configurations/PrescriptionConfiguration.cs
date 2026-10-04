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

// Dispensations of repeatable prescriptions (replicated). Mirrors the
// CREATE TABLE of the boot patch. The foreign key is on the medicine
// only: a dispensation follows its own sync register, so it may outlive
// a prescription deleted concurrently on another device
// (ApplyRemoteOperations). Removed with the medicine by
// MedicineDeletionRepository, with the prescription by DeletePrescription.
internal sealed class PrescriptionDispensationConfiguration : IEntityTypeConfiguration<PrescriptionDispensation>
{
    public void Configure(EntityTypeBuilder<PrescriptionDispensation> builder)
    {
        builder.ToTable("PrescriptionDispensations");
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => d.PrescriptionId);
        builder.HasIndex(d => d.MedicineId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(d => d.MedicineId)
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

// Device-local shortage notices (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.3): no foreign key, removed with the medicine by
// MedicineDeletionRepository and left out of sync images.
internal sealed class ShortageNoticeEventConfiguration : IEntityTypeConfiguration<MedReminder.Domain.Catalogue.ShortageNoticeEvent>
{
    public void Configure(EntityTypeBuilder<MedReminder.Domain.Catalogue.ShortageNoticeEvent> builder)
    {
        builder.ToTable("ShortageNoticeEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(16);
        builder.HasIndex(e => new { e.MedicineId, e.Code, e.Start }).IsUnique();
    }
}
