using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class LedgerCutoffConfiguration : IEntityTypeConfiguration<LedgerCutoff>
{
    public void Configure(EntityTypeBuilder<LedgerCutoff> builder)
    {
        builder.ToTable("LedgerCutoff");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
    }
}
