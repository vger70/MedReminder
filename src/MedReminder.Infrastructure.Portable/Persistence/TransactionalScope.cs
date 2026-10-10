using MedReminder.Application.Abstractions;

namespace MedReminder.Infrastructure.Persistence;

// EF Core implementation of ITransactionalScope over the scope's
// DbContext, the one the repositories and the unit of work share. On
// failure the transaction rolls back and the change tracker is cleared,
// so the entities it holds do not claim values the database no longer
// has.
internal sealed class TransactionalScope : ITransactionalScope
{
    private readonly MedReminderDbContext _dbContext;

    public TransactionalScope(MedReminderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            await work(cancellationToken);
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}
