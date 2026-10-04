namespace MedReminder.Domain.Prescriptions;

// A reminder to collect a prescription, shown by this device (not
// replicated). One per prescription and "valid until": a new date gets
// a new reminder.
public sealed class PrescriptionReminderEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid PrescriptionId { get; init; }

    public required Guid MedicineId { get; init; }

    public required DateOnly ValidUntil { get; init; }

    public required DateTimeOffset FiredAt { get; init; }
}
