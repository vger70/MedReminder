using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class SentEmailNotificationConfiguration
    : IEntityTypeConfiguration<SentEmailNotification>
{
    public void Configure(EntityTypeBuilder<SentEmailNotification> builder)
    {
        builder.ToTable("SentEmailNotifications");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.MedicineId, e.SentAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(e => e.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
