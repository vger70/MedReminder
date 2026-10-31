using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Persistence.Repositories;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Persistence;

// The bounded reads of issue #328 on real SQLite: a fact found by id,
// another medicine's fact or an unknown id read as null, and the
// interface default (over the whole history) reads the same as the EF
// implementation.
public class FactLookupTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    [Fact]
    public async Task An_intake_is_read_by_id_within_its_medicine()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();
        var id = await SeedMedicineAsync(ctx);
        var other = await SeedMedicineAsync(ctx);
        var intakes = Enumerable.Range(0, 3).Select(d => new MedicationIntake
            { MedicineId = id, Day = Today.AddDays(-d), Quantity = 1m, Status = IntakeStatus.Taken }).ToList();
        var foreign = new MedicationIntake
            { MedicineId = other, Day = Today, Quantity = 1m, Status = IntakeStatus.Taken };
        ctx.MedicationIntakes.AddRange([.. intakes, foreign]);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repository = new MedicationIntakeRepository(ctx);
        IMedicationIntakeRepository fallback = new IntakeHistoryOnly(repository);

        foreach (var reader in new IMedicationIntakeRepository[] { repository, fallback })
        {
            (await reader.GetAsync(id, intakes[1].Id, default))!.Day.Should().Be(Today.AddDays(-1));
            (await reader.GetAsync(id, foreign.Id, default)).Should().BeNull();
            (await reader.GetAsync(id, Guid.NewGuid(), default)).Should().BeNull();
        }
        // Untracked, as the history read: the caller removes it.
        ctx.ChangeTracker.Entries().Should().BeEmpty();
    }

    // The sum keeps decimal precision (10.05 less 0.1 a hundred times is
    // 0.05 exactly, which a SUM in SQL over REAL is not), clamps a negative
    // total to zero, leaves other medicines out, and matches
    // MedicineStock.Current over the history.
    [Fact]
    public async Task The_current_stock_is_the_exact_sum_of_the_deltas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();
        var id = await SeedMedicineAsync(ctx);
        var negative = await SeedMedicineAsync(ctx);
        var empty = await SeedMedicineAsync(ctx);
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        ctx.StockMovements.Add(Movement(id, start, StockMovementKind.InitialLoad, 10.05m));
        for (var i = 0; i < 100; i++)
        {
            ctx.StockMovements.Add(Movement(id, start.AddHours(i), StockMovementKind.Consumption, -0.1m));
        }
        ctx.StockMovements.Add(Movement(negative, start, StockMovementKind.InitialLoad, 1m));
        ctx.StockMovements.Add(Movement(negative, start.AddDays(1), StockMovementKind.NegativeCorrection, -3m));
        await ctx.SaveChangesAsync();
        var repository = new StockMovementRepository(ctx);
        IStockMovementRepository fallback = new StockHistoryOnly(repository);

        foreach (var reader in new[] { repository, fallback })
        {
            (await reader.GetCurrentStockAsync(id, default)).Should().Be(0.05m);
            (await reader.GetCurrentStockAsync(negative, default)).Should().Be(0m);
            (await reader.GetCurrentStockAsync(empty, default)).Should().Be(0m);
        }
    }

    // The latest new package with a positive quantity: a later negative
    // one (a typo corrected by hand) and other kinds are skipped.
    [Fact]
    public async Task The_last_new_package_quantity_is_the_latest_positive_one()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();
        var id = await SeedMedicineAsync(ctx);
        var other = await SeedMedicineAsync(ctx);
        var none = await SeedMedicineAsync(ctx);
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        ctx.StockMovements.AddRange(
            Movement(id, start, StockMovementKind.NewPackage, 28m),
            Movement(id, start.AddDays(10), StockMovementKind.NewPackage, 30m),
            Movement(id, start.AddDays(11), StockMovementKind.NewPackage, -5m),
            Movement(id, start.AddDays(12), StockMovementKind.ManualAdd, 99m),
            Movement(other, start.AddDays(20), StockMovementKind.NewPackage, 14m),
            Movement(none, start, StockMovementKind.InitialLoad, 20m));
        await ctx.SaveChangesAsync();
        var repository = new StockMovementRepository(ctx);
        IStockMovementRepository fallback = new StockHistoryOnly(repository);

        foreach (var reader in new[] { repository, fallback })
        {
            (await reader.GetLastNewPackageQuantityAsync(id, default)).Should().Be(30m);
            (await reader.GetLastNewPackageQuantityAsync(none, default)).Should().BeNull();
        }
    }

    [Fact]
    public async Task A_movement_is_read_by_id_within_its_medicine()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var ctx = fixture.CreateContext();
        var id = await SeedMedicineAsync(ctx);
        var other = await SeedMedicineAsync(ctx);
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var mine = Movement(id, start, StockMovementKind.NewPackage, 28m);
        var foreign = Movement(other, start, StockMovementKind.NewPackage, 28m);
        ctx.StockMovements.AddRange(mine, Movement(id, start.AddDays(1), StockMovementKind.Consumption, -1m), foreign);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var repository = new StockMovementRepository(ctx);
        IStockMovementRepository fallback = new StockHistoryOnly(repository);

        foreach (var reader in new[] { repository, fallback })
        {
            (await reader.GetAsync(id, mine.Id, default))!.QuantityDelta.Should().Be(28m);
            (await reader.GetAsync(id, foreign.Id, default)).Should().BeNull();
            (await reader.GetAsync(id, Guid.NewGuid(), default)).Should().BeNull();
        }
        ctx.ChangeTracker.Entries().Should().BeEmpty();
    }

    private static StockMovement Movement(Guid medicineId, DateTimeOffset at, StockMovementKind kind, decimal delta)
        => new() { MedicineId = medicineId, OccurredAt = at, Kind = kind, QuantityDelta = delta, StockEpoch = 1 };

    internal static async Task<Guid> SeedMedicineAsync(MedReminderDbContext ctx)
    {
        var medicine = new Medicine
        {
            Name = "Enalapril", Unit = "compresse", StartDate = Today.AddDays(-30),
            DosePerAdministration = 1m, AdministrationsPerDay = 1,
        };
        ctx.Medicines.Add(medicine);
        await ctx.SaveChangesAsync();
        return medicine.Id;
    }

    // Implement only the members without a default.
    private sealed class StockHistoryOnly(StockMovementRepository inner) : IStockMovementRepository
    {
        public Task<IReadOnlyList<StockMovement>> ListForMedicineAsync(Guid medicineId, CancellationToken ct)
            => inner.ListForMedicineAsync(medicineId, ct);

        public Task AddAsync(StockMovement movement, CancellationToken ct) => inner.AddAsync(movement, ct);

        public Task RemoveRangeAsync(IEnumerable<StockMovement> movements, CancellationToken ct)
            => inner.RemoveRangeAsync(movements, ct);

        public Task UpdateRangeAsync(IEnumerable<StockMovement> movements, CancellationToken ct)
            => inner.UpdateRangeAsync(movements, ct);

        public Task AddRangeAsync(IEnumerable<StockMovement> movements, CancellationToken ct)
            => inner.AddRangeAsync(movements, ct);

        public Task<DateOnly?> GetLastConsumptionDayAsync(Guid medicineId, CancellationToken ct)
            => inner.GetLastConsumptionDayAsync(medicineId, ct);
    }

    private sealed class IntakeHistoryOnly(MedicationIntakeRepository inner) : IMedicationIntakeRepository
    {
        public Task<IReadOnlyList<MedicationIntake>> ListForMedicineAsync(Guid medicineId, CancellationToken ct)
            => inner.ListForMedicineAsync(medicineId, ct);

        public Task<IReadOnlyList<DateOnly>> ListManualIntakeDaysAsync(Guid medicineId, DateOnly fromInclusive,
            DateOnly toInclusive, CancellationToken ct)
            => inner.ListManualIntakeDaysAsync(medicineId, fromInclusive, toInclusive, ct);

        public Task<IReadOnlyList<Guid>> ListMedicinesWithDayIntakeAsync(DateOnly day, CancellationToken ct)
            => inner.ListMedicinesWithDayIntakeAsync(day, ct);

        public Task AddAsync(MedicationIntake intake, CancellationToken ct) => inner.AddAsync(intake, ct);

        public Task RemoveAsync(MedicationIntake intake, CancellationToken ct) => inner.RemoveAsync(intake, ct);
    }
}
