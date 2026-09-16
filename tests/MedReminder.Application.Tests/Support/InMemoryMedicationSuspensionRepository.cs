using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryMedicationSuspensionRepository
    : IMedicationSuspensionRepository
{
    private readonly List<MedicationSuspension> _items = new();

    public Task<IReadOnlyList<MedicationSuspension>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MedicationSuspension> result =
            _items.Where(s => s.MedicineId == medicineId).ToList();
        return Task.FromResult(result);
    }

    public Task<MedicationSuspension?> GetOpenSuspensionAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var open = _items.FirstOrDefault(s => s.MedicineId == medicineId && s.EndDate is null);
        return Task.FromResult<MedicationSuspension?>(open);
    }

    public Task AddAsync(MedicationSuspension suspension, CancellationToken cancellationToken)
    {
        _items.Add(suspension);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(MedicationSuspension suspension, CancellationToken cancellationToken)
    {
        // È già lo stesso riferimento nella lista: nulla da fare per le
        // proprietà mutabili. Manteniamo il metodo per parità con l'API
        // del repository reale.
        _ = suspension;
        return Task.CompletedTask;
    }
}
