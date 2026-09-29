using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class SyncPeerConfiguration : IEntityTypeConfiguration<SyncPeer>
{
    public void Configure(EntityTypeBuilder<SyncPeer> builder)
    {
        builder.ToTable("SyncPeers");
        builder.HasKey(p => p.DeviceId);
    }
}
