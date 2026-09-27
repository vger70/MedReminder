using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class MedicineActivityRepository : IMedicineActivityRepository
{
    private readonly MedReminderDbContext _db;

    public MedicineActivityRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MedicineActivityChange>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => await _db.MedicineActivityChanges
            .AsNoTracking()
            .Where(a => a.MedicineId == medicineId)
            .OrderBy(a => a.RecordedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(MedicineActivityChange change, CancellationToken cancellationToken)
        => await _db.MedicineActivityChanges.AddAsync(change, cancellationToken);
}
