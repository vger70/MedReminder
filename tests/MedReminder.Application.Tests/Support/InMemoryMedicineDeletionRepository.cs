using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Tests.Support;

// Removes the medicine's rows from every in-memory repository of the
// scope, as MedicineDeletionRepository does on the database. The
// operation log is kept.
internal sealed class InMemoryMedicineDeletionRepository : IMedicineDeletionRepository
{
    private readonly ApplicationTestScope _scope;

    public InMemoryMedicineDeletionRepository(ApplicationTestScope scope)
    {
        _scope = scope;
    }

    public Task RemoveAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        _scope.Slots.RemoveForMedicine(medicineId);
        _scope.Schedules.RemoveForMedicine(medicineId);
        _scope.Activity.RemoveForMedicine(medicineId);
        _scope.Stock.RemoveForMedicine(medicineId);
        _scope.Intakes.RemoveForMedicine(medicineId);
        _scope.Counts.RemoveForMedicine(medicineId);
        _scope.Suspensions.RemoveForMedicine(medicineId);
        _scope.Retractions.RemoveForMedicine(medicineId);
        _scope.Notifications.RemoveForMedicine(medicineId);
        _scope.SentEmails.RemoveForMedicine(medicineId);
        _scope.DoseEvents.RemoveForMedicine(medicineId);
        _scope.Prescriptions.RemoveForMedicine(medicineId);
        _scope.PrescriptionReminderEvents.RemoveForMedicine(medicineId);
        _scope.Deadlines.RemoveForMedicine(medicineId);
        _scope.DeadlineReminderEvents.RemoveForMedicine(medicineId);
        _scope.Packages.RemoveForMedicine(medicineId);
        _scope.SyncVersions.RemoveForMedicine(medicineId);
        _scope.SyncConflicts.RemoveForMedicine(medicineId);
        _scope.Medicines.RemoveForMedicine(medicineId);
        return Task.CompletedTask;
    }
}
