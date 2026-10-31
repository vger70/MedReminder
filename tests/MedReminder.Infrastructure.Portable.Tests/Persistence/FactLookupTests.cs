using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
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

    // Implements only the members without a default.
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
