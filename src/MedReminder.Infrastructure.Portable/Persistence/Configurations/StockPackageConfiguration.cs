using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Mirrors the CREATE TABLE of the package boot patch in
// DatabaseInitializer.
internal sealed class StockPackageConfiguration : IEntityTypeConfiguration<StockPackage>
{
    public void Configure(EntityTypeBuilder<StockPackage> builder)
    {
        builder.ToTable("StockPackages");
        builder.HasKey(p => p.Id);

        // Decimal as text, like StockMovements.QuantityDelta: SQLite has
        // no decimal type.
        builder.Property(p => p.Quantity).HasConversion<string>();
        builder.Property(p => p.Closure).HasConversion<int?>();
        builder.Property(p => p.Batch).HasMaxLength(PackageExpiryRules.MaxBatchLength);
        builder.Ignore(p => p.IsClosed);

        builder.HasIndex(p => p.MedicineId);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(p => p.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Device-local: no foreign key, like DeadlineReminderEvents; removed with
// the medicine by MedicineDeletionRepository.
internal sealed class PackageExpiryNoticeEventConfiguration : IEntityTypeConfiguration<PackageExpiryNoticeEvent>
{
    public void Configure(EntityTypeBuilder<PackageExpiryNoticeEvent> builder)
    {
        builder.ToTable("PackageExpiryNoticeEvents");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PackageId, e.EffectiveExpiry, e.Stage }).IsUnique();
    }
}
