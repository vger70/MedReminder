using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicationIntakeRepository : IMedicationIntakeRepository
{
    private readonly List<MedicationIntake> _items = new();

    public IReadOnlyList<MedicationIntake> All => _items;

    public Task<IReadOnlyList<MedicationIntake>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MedicationIntake> result =
            _items.Where(i => i.MedicineId == medicineId)
                  .OrderBy(i => i.Day)
                  .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DateOnly>> ListManualIntakeDaysAsync(
        Guid medicineId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DateOnly> result = _items
            .Where(i => i.MedicineId == medicineId
                        && i.Day >= fromInclusive
                        && i.Day <= toInclusive)
            .Select(i => i.Day)
            .Distinct()
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Guid>> ListMedicinesWithDayIntakeAsync(DateOnly day, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Guid>>(
            [.. _items.Where(i => i.Day == day && !i.IsExtra).Select(i => i.MedicineId).Distinct()]);

    public Task AddAsync(MedicationIntake intake, CancellationToken cancellationToken)
    {
        _items.Add(intake);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(MedicationIntake intake, CancellationToken cancellationToken)
    {
        _items.RemoveAll(i => i.Id == intake.Id);
        return Task.CompletedTask;
    }

    // Test double of IMedicineDeletionRepository (InMemoryMedicineDeletionRepository).
    public void RemoveForMedicine(Guid medicineId)
    {
        _items.RemoveAll(x => x.MedicineId == medicineId);
    }
}
