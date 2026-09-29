using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("StockCounts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CountedQuantity).HasConversion<string>();
        builder.Property(c => c.TakenToday).HasConversion<string>();
        builder.Property(c => c.LedgerAtStartOfDay).HasConversion<string>();
        builder.Property(c => c.CountDayScheduled).HasConversion<string>();
        builder.Property(c => c.Correction).HasConversion<string>();
        builder.Property(c => c.Notes).HasMaxLength(500);

        builder.HasIndex(c => c.MedicineId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(c => c.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
