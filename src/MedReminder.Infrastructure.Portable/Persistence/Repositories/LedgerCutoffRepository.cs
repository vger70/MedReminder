using MedReminder.Application.Abstractions;
using MedReminder.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Persistence.Repositories;

internal sealed class LedgerCutoffRepository : ILedgerCutoffRepository
{
    private readonly MedReminderDbContext _db;

    public LedgerCutoffRepository(MedReminderDbContext db)
    {
        _db = db;
    }

    public async Task<LedgerCutoff?> GetAsync(CancellationToken cancellationToken)
        => await _db.LedgerCutoffs.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
}
