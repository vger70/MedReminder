using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicationScheduleHistoryConfiguration
    : IEntityTypeConfiguration<MedicationScheduleHistory>
{
    public void Configure(EntityTypeBuilder<MedicationScheduleHistory> builder)
    {
        builder.ToTable("MedicationScheduleHistories");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.DosePerAdministration).HasConversion<string>();
        builder.HasIndex(s => new { s.MedicineId, s.EffectiveFrom });

        // A1: discriminated schedule shape carried alongside the
        // legacy Dose / AdministrationsPerDay fields. Persisted as
        // INTEGER with default 0 (FixedDaily) so that pre-A1 rows
        // read back with legacy semantics — see
        // ANALYSIS-A1-REGIMENS.md §2.4.
        builder.Property(s => s.ScheduleKind)
            .HasDefaultValue(ScheduleKind.FixedDaily);
        builder.Property(s => s.SchedulePayload)
            .IsRequired(false);

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
