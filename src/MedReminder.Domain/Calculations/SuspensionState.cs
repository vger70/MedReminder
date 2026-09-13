using MedReminder.Domain.Medicines;

namespace MedReminder.Domain.Calculations;

// Verifica se una data cade in un periodo di sospensione della terapia.
// Una sospensione con EndDate == null è aperta (in corso).
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
