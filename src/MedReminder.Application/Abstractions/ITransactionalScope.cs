namespace MedReminder.Application.Abstractions;

// Runs several steps of one DI scope in a single database transaction:
// either all their saves persist or none does. A step keeps calling
// IUnitOfWork.SaveChangesAsync as usual; inside the scope those saves join
// the transaction and are committed together at the end.
//
// Call it only under WriteGate, around ungated steps (see EditMedicine):
// the transaction takes the SQLite write lock, which must follow the gate,
// and a gated step inside it would release the gate mid-transaction.
public interface ITransactionalScope
{
    Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
