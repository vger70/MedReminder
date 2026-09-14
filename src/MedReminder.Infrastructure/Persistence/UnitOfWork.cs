using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Persistence;

// Implementazione EF Core dell'unità di lavoro: SaveChangesAsync sul
// DbContext condiviso persiste tutte le modifiche accumulate dai
// repository nella stessa scope DI.
internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly MedReminderDbContext _dbContext;

    public UnitOfWork(MedReminderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
