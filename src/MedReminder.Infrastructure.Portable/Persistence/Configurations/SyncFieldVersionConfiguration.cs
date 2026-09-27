using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Register versions (B.1 Phase 3b). No foreign keys: a version can
// arrive before the fact it belongs to, or outlive a retracted one.
internal sealed class SyncFieldVersionConfiguration : IEntityTypeConfiguration<SyncFieldVersion>
{
    public void Configure(EntityTypeBuilder<SyncFieldVersion> builder)
    {
        builder.ToTable("SyncFieldVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Register).IsRequired();
        builder.Ignore(v => v.Version);
        builder.Ignore(v => v.Base);

        builder.HasIndex(v => v.EntityId);
    }
}
