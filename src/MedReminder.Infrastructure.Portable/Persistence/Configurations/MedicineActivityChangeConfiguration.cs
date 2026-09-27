using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicineActivityChangeConfiguration
    : IEntityTypeConfiguration<MedicineActivityChange>
{
    public void Configure(EntityTypeBuilder<MedicineActivityChange> builder)
    {
        builder.ToTable("MedicineActivityChanges");
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.MedicineId, a.RecordedAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(a => a.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
