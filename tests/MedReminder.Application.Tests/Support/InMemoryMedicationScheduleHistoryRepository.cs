using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicationScheduleHistoryRepository
    : IMedicationScheduleHistoryRepository
{
    private readonly List<MedicationScheduleHistory> _items = new();

    public Task<IReadOnlyList<MedicationScheduleHistory>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MedicationScheduleHistory> result =
            _items.Where(s => s.MedicineId == medicineId).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(MedicationScheduleHistory entry, CancellationToken cancellationToken)
    {
        _items.Add(entry);
        return Task.CompletedTask;
    }
}
