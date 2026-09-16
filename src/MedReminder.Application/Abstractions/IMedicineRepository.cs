using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Abstractions;

public interface IMedicineRepository
{
    Task<Medicine?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Medicine>> ListActiveAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Medicine>> ListAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Medicine medicine, CancellationToken cancellationToken);
    Task UpdateAsync(Medicine medicine, CancellationToken cancellationToken);
}
