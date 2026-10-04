namespace MedReminder.Domain.Deadlines;

// A deadline reminder shown by this device (not replicated). One per
// deadline and due date: the next date of a recurring deadline, or a
// changed date, gets a new reminder.
public sealed class DeadlineReminderEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid DeadlineId { get; init; }

    // Null for a deadline of the profile; lets the deletion of a medicine
    // remove the reminders of its deadlines.
    public Guid? MedicineId { get; init; }

    public required DateOnly DueOn { get; init; }

    public required DateTimeOffset FiredAt { get; init; }
}
