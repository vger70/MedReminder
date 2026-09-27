using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Local operation log (B.1 Phase 3a). No foreign key to Medicines: from
// Phase 3b operations of other devices are recorded as they arrive,
// whatever their order.
internal sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
{
    public void Configure(EntityTypeBuilder<SyncOperation> builder)
    {
        builder.ToTable("SyncOperations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type).IsRequired();
        builder.Property(o => o.Payload).IsRequired();
        builder.Ignore(o => o.Timestamp);

        builder.HasIndex(o => new { o.HlcPhysicalMs, o.HlcCounter });
        builder.HasIndex(o => o.SegmentSeq);
    }
}
