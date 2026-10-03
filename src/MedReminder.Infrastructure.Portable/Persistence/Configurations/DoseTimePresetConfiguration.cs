using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

// Device-local, not replicated (DoseTimePreset). Same shape as the boot
// patch in DatabaseInitializer.
internal sealed class DoseTimePresetConfiguration : IEntityTypeConfiguration<DoseTimePreset>
{
    public void Configure(EntityTypeBuilder<DoseTimePreset> builder)
    {
        builder.ToTable("DoseTimePresets");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.BuiltInKey).HasMaxLength(64);
        builder.Property(p => p.Label).HasMaxLength(200);
        builder.Property(p => p.Time);
        builder.Property(p => p.IsAsNeeded);
        builder.Property(p => p.Order);
        builder.Property(p => p.IsHidden);
    }
}

internal sealed class DoseTimeDefaultConfiguration : IEntityTypeConfiguration<DoseTimeDefault>
{
    public void Configure(EntityTypeBuilder<DoseTimeDefault> builder)
    {
        builder.ToTable("DoseTimeDefaults");
        builder.HasKey(d => d.AdministrationsPerDay);
        builder.Property(d => d.AdministrationsPerDay).ValueGeneratedNever();
        builder.Property(d => d.Times).HasMaxLength(64);
    }
}
