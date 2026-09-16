using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class NotificationEventConfiguration
    : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("NotificationEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Channel).HasConversion<int>();
        builder.Property(e => e.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(e => e.MedicineId);
        builder.HasIndex(e => new { e.MedicineId, e.TriggeredAt });

        builder.HasOne<Medicine>()
            .WithMany()
            .HasForeignKey(e => e.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
