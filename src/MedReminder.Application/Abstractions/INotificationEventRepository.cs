using MedReminder.Domain.Notifications;

namespace MedReminder.Application.Abstractions;

public interface INotificationEventRepository
{
    // Ultima notifica registrata per la medicina (a prescindere da esito e
    // da epoch): la logica di soppressione la valuta NotificationCycle.
    Task<NotificationEvent?> GetLatestForMedicineAsync(
        Guid medicineId,
        CancellationToken cancellationToken);

    Task AddAsync(NotificationEvent evt, CancellationToken cancellationToken);
}
