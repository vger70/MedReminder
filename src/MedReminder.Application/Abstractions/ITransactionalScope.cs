namespace MedReminder.Application.Abstractions;

// Runs several use cases of one DI scope in a single database
// transaction: either all their saves persist or none does. A use case
// keeps calling IUnitOfWork.SaveChangesAsync as usual; inside the scope
// those saves join the transaction and are committed together at the end.
public interface ITransactionalScope
{
    Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
