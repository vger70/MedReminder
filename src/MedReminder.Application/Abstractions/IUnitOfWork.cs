namespace MedReminder.Application.Abstractions;

// Unit of work: groups multiple writes into a single atomic commit.
// For the MVP, SaveChangesAsync is enough — EF Core already guarantees
// the transactionality of a single SaveChanges call.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
