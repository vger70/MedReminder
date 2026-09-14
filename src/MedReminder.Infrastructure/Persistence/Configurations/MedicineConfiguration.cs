using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedReminder.Infrastructure.Persistence.Configurations;

internal sealed class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> builder)
    {
        builder.ToTable("Medicines");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.ActiveIngredient).HasMaxLength(200);
        builder.Property(m => m.Package).HasMaxLength(200);
        builder.Property(m => m.Unit).IsRequired().HasMaxLength(40);
        builder.Property(m => m.DoctorName).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        // SQLite non ha decimal nativo. Uso HasConversion<string> per
        // memorizzare come TEXT preservando la precisione (evita gli
        // arrotondamenti che REAL introdurrebbe).
        builder.Property(m => m.DosePerAdministration).HasConversion<string>();

        builder.Property(m => m.NotificationChannels).HasConversion<int>();

        builder.HasIndex(m => m.IsActive);
        builder.HasIndex(m => m.Name);
    }
}
