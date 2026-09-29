using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Checks whether a date falls inside a therapy suspension period. A
// suspension with EndDate == null is open (ongoing).
public static class SuspensionState
{
    public static bool IsSuspendedOn(
        DateOnly day,
        IEnumerable<MedicationSuspension> suspensions)
    {
        ArgumentNullException.ThrowIfNull(suspensions);
        foreach (var s in suspensions)
        {
            if (day < s.StartDate) continue;
            if (s.EndDate is null || day <= s.EndDate) return true;
        }
        return false;
    }
}
