using MedReminder.Application.Abstractions;

namespace MedReminder.Application.Calendar;

// The events MedReminder puts in a calendar. Calendars usually live on
// a third-party cloud, so the titles are generic unless the user asks
// for the names: a medicine name, a deadline description or any other
// health detail appears only then.
public static class CalendarEntries
{
    public static CalendarEvent Reorder(Guid medicineId, DateOnly date, string? medicineName, ILocalizationService? loc)
        => new($"reorder-{medicineId:N}@medreminder", date,
            medicineName is null
                ? Text(loc, "Calendar.Reorder.Generic", "MedReminder: request a prescription")
                : Text(loc, "Calendar.Reorder.Named", "Request a prescription: {0}", medicineName),
            Text(loc, "Calendar.Description", "Details in MedReminder."));

    public static CalendarEvent RunOut(Guid medicineId, DateOnly date, string? medicineName, ILocalizationService? loc)
        => new($"runout-{medicineId:N}@medreminder", date,
            medicineName is null
                ? Text(loc, "Calendar.RunOut.Generic", "MedReminder: a medicine runs out")
                : Text(loc, "Calendar.RunOut.Named", "{0} runs out", medicineName),
            Text(loc, "Calendar.Description", "Details in MedReminder."));

    public static CalendarEvent Prescription(Guid prescriptionId, DateOnly validUntil, string? medicineName,
        ILocalizationService? loc)
        => new($"prescription-{prescriptionId:N}@medreminder", validUntil,
            medicineName is null
                ? Text(loc, "Calendar.Prescription.Generic", "MedReminder: collect a prescription")
                : Text(loc, "Calendar.Prescription.Named", "Collect the prescription: {0}", medicineName),
            Text(loc, "Calendar.Description", "Details in MedReminder."));

    public static CalendarEvent Deadline(Guid deadlineId, DateOnly dueOn, string? subject, ILocalizationService? loc)
        => new($"deadline-{deadlineId:N}@medreminder", dueOn,
            subject is null
                ? Text(loc, "Calendar.Deadline.Generic", "MedReminder: deadline")
                : Text(loc, "Calendar.Deadline.Named", "Deadline: {0}", subject),
            Text(loc, "Calendar.Description", "Details in MedReminder."));

    private static string Text(ILocalizationService? loc, string key, string fallback, params object[] args)
        => loc is null ? string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args) : loc.Get(key, args);
}
