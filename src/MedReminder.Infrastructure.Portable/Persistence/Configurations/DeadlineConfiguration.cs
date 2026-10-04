using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Mirrors the CREATE TABLE of the deadline boot patch in
// DatabaseInitializer.
internal sealed class DeadlineConfiguration : IEntityTypeConfiguration<Deadline>
{
    public void Configure(EntityTypeBuilder<Deadline> builder)
    {
        builder.ToTable("Deadlines");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Kind).HasConversion<int>();
        builder.Property(d => d.Channels).HasConversion<int>();
        builder.Property(d => d.Label).HasMaxLength(DeadlineRules.MaxLabelLength);

        builder.HasIndex(d => d.MedicineId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(d => d.MedicineId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Device-local: no foreign key, like PrescriptionReminderEvents; removed
// with the medicine by MedicineDeletionRepository.
internal sealed class DeadlineReminderEventConfiguration : IEntityTypeConfiguration<DeadlineReminderEvent>
{
    public void Configure(EntityTypeBuilder<DeadlineReminderEvent> builder)
    {
        builder.ToTable("DeadlineReminderEvents");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.DeadlineId, e.DueOn }).IsUnique();
    }
}
