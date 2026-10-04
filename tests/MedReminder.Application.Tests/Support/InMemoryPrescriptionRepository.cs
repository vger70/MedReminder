using MedReminder.Application.Abstractions;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Tests.Support;

internal sealed class InMemoryPrescriptionRepository : IPrescriptionRepository
{
    private readonly List<Prescription> _items = new();

    public IReadOnlyList<Prescription> All => _items;

    public Task<Prescription?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Prescription>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Prescription>>(_items.ToList());

    public Task<IReadOnlyList<Prescription>> ListForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Prescription>>(_items.Where(p => p.MedicineId == medicineId).ToList());

    public Task AddAsync(Prescription prescription, CancellationToken cancellationToken)
    {
        _items.Add(prescription);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Prescription prescription, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(p => p.Id == prescription.Id);
        if (index >= 0) _items[index] = prescription;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Prescription prescription, CancellationToken cancellationToken)
    {
        _items.RemoveAll(p => p.Id == prescription.Id);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(p => p.MedicineId == medicineId);
}

internal sealed class InMemoryPrescriptionDispensationRepository : IPrescriptionDispensationRepository
{
    private readonly List<PrescriptionDispensation> _items = new();

    public IReadOnlyList<PrescriptionDispensation> All => _items;

    public Task<PrescriptionDispensation?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_items.FirstOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<PrescriptionDispensation>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PrescriptionDispensation>>(_items.ToList());

    public Task<IReadOnlyList<PrescriptionDispensation>> ListForPrescriptionAsync(
        Guid prescriptionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PrescriptionDispensation>>(
            _items.Where(d => d.PrescriptionId == prescriptionId).ToList());

    public Task<IReadOnlyList<PrescriptionDispensation>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PrescriptionDispensation>>(_items.Where(d => d.MedicineId == medicineId).ToList());

    public Task AddAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken)
    {
        _items.Add(dispensation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(d => d.Id == dispensation.Id);
        if (index >= 0) _items[index] = dispensation;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(PrescriptionDispensation dispensation, CancellationToken cancellationToken)
    {
        _items.RemoveAll(d => d.Id == dispensation.Id);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(d => d.MedicineId == medicineId);
}

internal sealed class InMemoryPrescriptionReminderEventRepository : IPrescriptionReminderEventRepository
{
    private readonly List<PrescriptionReminderEvent> _items = new();

    public IReadOnlyList<PrescriptionReminderEvent> All => _items;

    public Task<bool> ExistsAsync(Guid prescriptionId, DateOnly validUntil, CancellationToken cancellationToken)
        => Task.FromResult(_items.Any(e => e.PrescriptionId == prescriptionId && e.ValidUntil == validUntil));

    public Task AddAsync(PrescriptionReminderEvent reminder, CancellationToken cancellationToken)
    {
        _items.Add(reminder);
        return Task.CompletedTask;
    }

    public void RemoveForMedicine(Guid medicineId) => _items.RemoveAll(e => e.MedicineId == medicineId);
}
