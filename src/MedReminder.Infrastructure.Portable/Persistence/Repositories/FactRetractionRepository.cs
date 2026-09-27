using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class FactRetractionRepository : IFactRetractionRepository
{
    private readonly MedReminderDbContext _db;

    public FactRetractionRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FactRetraction>> ListForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
        => await _db.FactRetractions
            .AsNoTracking()
            .Where(r => r.MedicineId == medicineId)
            .OrderBy(r => r.RecordedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(FactRetraction retraction, CancellationToken cancellationToken)
        => await _db.FactRetractions.AddAsync(retraction, cancellationToken);
}
