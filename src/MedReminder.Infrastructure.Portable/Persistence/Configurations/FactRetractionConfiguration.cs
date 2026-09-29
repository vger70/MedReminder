using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class FactRetractionConfiguration : IEntityTypeConfiguration<FactRetraction>
{
    public void Configure(EntityTypeBuilder<FactRetraction> builder)
    {
        builder.ToTable("FactRetractions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Kind).HasConversion<int>();

        builder.HasIndex(r => r.MedicineId);
        builder.HasIndex(r => r.FactId).IsUnique();

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(r => r.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
