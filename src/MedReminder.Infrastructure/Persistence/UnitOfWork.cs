using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Persistence;

// EF Core implementation of the unit of work: SaveChangesAsync on
// the shared DbContext persists every change accumulated by the
// repositories in the same DI scope.
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
