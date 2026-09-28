namespace MedReminder.Application.Abstractions;

// Removal of a medicine with every row that refers to it: schedule rows,
// slot sets and slots, activity changes, stock movements, intakes,
// counts, suspensions, retraction tombstones, notification and
// dose-reminder events, and its sync register versions and conflicts.
// The operation log (SyncOperations) is kept: it holds the
// MedicineDeleted tombstone the apply step checks. The removal is staged
// on the unit of work and committed by IUnitOfWork.SaveChangesAsync.
public interface IMedicineDeletionRepository
{
    Task RemoveAsync(Guid medicineId, CancellationToken cancellationToken);
}
