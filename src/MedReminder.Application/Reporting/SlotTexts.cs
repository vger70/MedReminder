using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Reporting;

// Text shared by the therapy card and the emails that list the slots.
internal static class SlotTexts
{
    private const string AsNeededEn = "as needed";

    // " (as needed)" in the app language for an as-needed slot; empty
    // for a scheduled slot, and when the description already says it
    // (the "As needed" preset). English without a localization service.
    public static string AsNeededSuffix(MedicationAdministrationSlot slot, ILocalizationService? loc)
    {
        if (!slot.IsAsNeeded) return string.Empty;
        var marker = loc?.Get("Reports.Therapy.AsNeeded") ?? AsNeededEn;
        return string.Equals(slot.TimingLabel?.Trim(), marker, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : " (" + marker + ")";
    }
}
