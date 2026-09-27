using MedReminder.Domain.Sync;

namespace MedReminder.Application.Abstractions;

// Captures the operations a use case produces (B.1 Phase 3a,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §7.2): each operation gets
// the next hybrid-clock timestamp and is added to the local log in the
// caller's unit of work, so it commits or rolls back with the write it
// describes. Does not call SaveChangesAsync.
//
// When sync is not enabled for the profile, AppendAsync records nothing.
public interface IOperationLog
{
    Task AppendAsync(IReadOnlyList<SyncOperationBody> operations, CancellationToken cancellationToken);
}
