namespace MedReminder.Domain.Notifications;

// Records one fired dose-time reminder, keyed on
// (MedicineId, SlotKey, LocalDate), so the scheduler can
// guarantee at-most-one-fire per (medicine, slot, calendar day).
// See ANALYSIS-A5-DOSE-TIME-REMINDER.md §3.2.
public sealed class DoseReminderEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Stable slot identifier: MedicationAdministrationSlot.Id.ToString()
    // when the slot has a persistent Id; "HH:mm" fallback otherwise.
    public required string SlotKey { get; init; }

    // Calendar date in local time when the reminder fired. DateOnly
    // so the "once per day" guarantee spans DST transitions correctly
    // (local date, not UTC date).
    public required DateOnly LocalDate { get; init; }

    // Exact timestamp of the successful dispatch (UTC ticks).
    public required DateTimeOffset FiredAt { get; init; }

    public required NotificationChannels Channel { get; init; }
}
