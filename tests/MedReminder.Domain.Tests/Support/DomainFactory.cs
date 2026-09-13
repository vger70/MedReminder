using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Domain.Tests.Support;

// Piccoli helper: creano istanze del dominio con default sensati e
// consentono override chirurgici. Servono solo ai test di questa
// assembly.
internal static class DomainFactory
{
    public static readonly Guid MedicineId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Medicine Medicine(
        string name = "Enalapril",
        decimal dosePerAdministration = 1m,
        int administrationsPerDay = 2,
        int thresholdDays = 7,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        bool isActive = true,
        int stockEpoch = 1,
        NotificationChannels channels = NotificationChannels.Windows)
    {
        return new Medicine
        {
            Id = MedicineId,
            Name = name,
            Unit = "compresse",
            DosePerAdministration = dosePerAdministration,
            AdministrationsPerDay = administrationsPerDay,
            StartDate = startDate ?? new DateOnly(2026, 1, 1),
            EndDate = endDate,
            ThresholdDays = thresholdDays,
            IsActive = isActive,
            StockEpoch = stockEpoch,
            NotificationChannels = channels,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    public static MedicationScheduleHistory Schedule(
        DateOnly effectiveFrom,
        decimal dosePerAdministration,
        int administrationsPerDay,
        Guid? medicineId = null)
    {
        return new MedicationScheduleHistory
        {
            MedicineId = medicineId ?? MedicineId,
            EffectiveFrom = effectiveFrom,
            DosePerAdministration = dosePerAdministration,
            AdministrationsPerDay = administrationsPerDay,
        };
    }

    public static MedicationSuspension Suspension(
        DateOnly startDate,
        DateOnly? endDate = null,
        Guid? medicineId = null)
    {
        return new MedicationSuspension
        {
            MedicineId = medicineId ?? MedicineId,
            StartDate = startDate,
            EndDate = endDate,
        };
    }

    public static StockMovement Movement(
        StockMovementKind kind,
        decimal delta,
        int epoch = 1,
        Guid? medicineId = null,
        DateTimeOffset? occurredAt = null)
    {
        return new StockMovement
        {
            MedicineId = medicineId ?? MedicineId,
            OccurredAt = occurredAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Kind = kind,
            QuantityDelta = delta,
            StockEpoch = epoch,
        };
    }

    public static NotificationEvent NotificationEventForEpoch(
        int epoch,
        bool success = true,
        Guid? medicineId = null)
    {
        return new NotificationEvent
        {
            MedicineId = medicineId ?? MedicineId,
            StockEpoch = epoch,
            TriggeredAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Channel = NotificationChannels.Windows,
            DaysRemainingAtSend = 3,
            Success = success,
        };
    }
}
