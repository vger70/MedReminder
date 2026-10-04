using MedReminder.Application.Abstractions;
using MedReminder.Domain.Deadlines;

namespace MedReminder.Application.Deadlines;

// The words shown for a deadline: its label when it has one, else the
// name of its kind, with the medicine when it belongs to one.
public static class DeadlineTexts
{
    public static string Kind(DeadlineKind kind, ILocalizationService? localization)
        => localization?.Get("Deadlines.Kind." + kind) ?? kind switch
        {
            DeadlineKind.TherapeuticPlan => "Therapeutic plan",
            DeadlineKind.ExemptionRenewal => "Exemption renewal",
            DeadlineKind.CheckUp => "Check-up",
            _ => "Deadline",
        };

    public static string Subject(Deadline deadline, string? medicineName, ILocalizationService? localization)
    {
        ArgumentNullException.ThrowIfNull(deadline);
        var what = string.IsNullOrWhiteSpace(deadline.Label) ? Kind(deadline.Kind, localization) : deadline.Label!;
        return string.IsNullOrEmpty(medicineName) ? what : $"{what} — {medicineName}";
    }
}
