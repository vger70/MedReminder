using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Kind).HasConversion<int>();
        builder.Property(m => m.QuantityDelta).HasConversion<string>();
        builder.Property(m => m.Notes).HasMaxLength(500);

        builder.HasIndex(m => m.MedicineId);
        builder.HasIndex(m => new { m.MedicineId, m.Kind, m.OccurredAt });

        // Referential constraint towards Medicine: cascade delete not
        // desired (we prefer deactivation over deletion, spec §14).
        // Configure the FK with Restrict.
        builder.HasOne<Domain.Medicines.Medicine>()
            .WithMany()
            .HasForeignKey(m => m.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
