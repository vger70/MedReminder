using MedReminder.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Conflict list (B.1 Phase 3b).
internal sealed class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("SyncConflicts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Kind).HasConversion<int>();

        builder.HasIndex(c => c.SubjectId);
        builder.HasIndex(c => c.MedicineId);
    }
}
