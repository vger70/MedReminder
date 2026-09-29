using FluentAssertions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// Verifica il round-trip dei tipi problematici su SQLite (DateOnly,
// DateTimeOffset, decimal, enum, Guid, Flags): after a write → read
// round-trip the values must come back equal without loss.
public class RoundTripTests
{
    [Fact]
    public async Task Medicine_survives_write_and_read()
    {
        using var fixture = new SqliteInMemoryFixture();
        var expected = new Medicine
        {
            Name = "Enalapril",
            Unit = "compresse",
            DosePerAdministration = 2.5m,
            AdministrationsPerDay = 3,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2027, 3, 1),
            ThresholdDays = 7,
            DoctorName = "Dr. Rossi",
            Notes = "note",
            IsActive = true,
            StockEpoch = 4,
            NotificationChannels = NotificationChannels.Both,
            CreatedAt = new DateTimeOffset(2026, 9, 13, 8, 30, 15, TimeSpan.FromHours(2)),
            UpdatedAt = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.FromHours(2)),
        };

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new MedicineRepository(ctx);
            await repo.AddAsync(expected, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new MedicineRepository(ctx);
            var loaded = await repo.GetAsync(expected.Id, CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be(expected.Name);
            loaded.DosePerAdministration.Should().Be(2.5m);
            loaded.NotificationChannels.Should().Be(NotificationChannels.Both);
            loaded.StartDate.Should().Be(expected.StartDate);
            loaded.EndDate.Should().Be(expected.EndDate);
            loaded.CreatedAt.Should().Be(expected.CreatedAt);
            loaded.StockEpoch.Should().Be(4);
        }
    }

    [Fact]
    public async Task Stock_movement_preserves_kind_and_signed_delta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(new Medicine
            {
                Id = medicineId,
                Name = "M",
                Unit = "cp",
                DosePerAdministration = 1m,
                AdministrationsPerDay = 1,
                StartDate = new DateOnly(2026, 9, 1),
                ThresholdDays = 7,
                NotificationChannels = NotificationChannels.Windows,
                StockEpoch = 1,
            });
            ctx.StockMovements.AddRange(
                new StockMovement
                {
                    MedicineId = medicineId,
                    OccurredAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                    Kind = StockMovementKind.InitialLoad,
                    QuantityDelta = 30m,
                    StockEpoch = 1,
                },
                new StockMovement
                {
                    MedicineId = medicineId,
                    OccurredAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                    Kind = StockMovementKind.Consumption,
                    QuantityDelta = -2m,
                    StockEpoch = 1,
                });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new StockMovementRepository(ctx);
            var loaded = await repo.ListForMedicineAsync(medicineId, CancellationToken.None);
            loaded.Should().HaveCount(2);
            loaded[0].Kind.Should().Be(StockMovementKind.InitialLoad);
            loaded[0].QuantityDelta.Should().Be(30m);
            loaded[1].Kind.Should().Be(StockMovementKind.Consumption);
            loaded[1].QuantityDelta.Should().Be(-2m);
        }
    }

    [Fact]
    public async Task GetLastConsumptionDay_returns_maximum_day_across_movements()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(new Medicine
            {
                Id = medicineId, Name = "M", Unit = "cp",
                DosePerAdministration = 1m, AdministrationsPerDay = 1,
                StartDate = new DateOnly(2026, 9, 1), ThresholdDays = 7,
                NotificationChannels = NotificationChannels.Windows, StockEpoch = 1,
            });
            ctx.StockMovements.AddRange(
                new StockMovement { MedicineId = medicineId,
                    OccurredAt = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
                    Kind = StockMovementKind.Consumption, QuantityDelta = -1m, StockEpoch = 1 },
                new StockMovement { MedicineId = medicineId,
                    OccurredAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
                    Kind = StockMovementKind.Consumption, QuantityDelta = -1m, StockEpoch = 1 },
                new StockMovement { MedicineId = medicineId,
                    OccurredAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
                    Kind = StockMovementKind.NewPackage, QuantityDelta = 30m, StockEpoch = 2 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new StockMovementRepository(ctx);
            var last = await repo.GetLastConsumptionDayAsync(medicineId, CancellationToken.None);
            last.Should().Be(new DateOnly(2026, 9, 8));   // the NewPackage is not a Consumption
        }
    }

    [Fact]
    public async Task Medication_suspension_preserves_open_end_date()
    {
        using var fixture = new SqliteInMemoryFixture();
        var medicineId = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Medicines.Add(new Medicine
            {
                Id = medicineId, Name = "M", Unit = "cp",
                DosePerAdministration = 1m, AdministrationsPerDay = 1,
                StartDate = new DateOnly(2026, 9, 1), ThresholdDays = 7,
                NotificationChannels = NotificationChannels.Windows, StockEpoch = 1,
            });
            ctx.MedicationSuspensions.Add(new MedicationSuspension
            {
                MedicineId = medicineId,
                StartDate = new DateOnly(2026, 9, 5),
                EndDate = null,
                Reason = "influenza",
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = fixture.CreateContext())
        {
            var repo = new MedicationSuspensionRepository(ctx);
            var open = await repo.GetOpenSuspensionAsync(medicineId, CancellationToken.None);
            open.Should().NotBeNull();
            open!.EndDate.Should().BeNull();
            open.Reason.Should().Be("influenza");
        }
    }
}
