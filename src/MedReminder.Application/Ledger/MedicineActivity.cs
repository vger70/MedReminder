using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Ledger;

// Records an activation / deactivation as a dated fact (B.1 Phase 2c-2,
// D15), shared by UpdateMedicine and DeactivateMedicine. The change
// takes effect from today; nothing is recorded when the value does not
// change.
internal static class MedicineActivity
{
    public static async Task RecordAsync(
        IMedicineActivityRepository activity,
        Medicine medicine,
        bool active,
        DateTimeOffset now,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        if (medicine.IsActive == active) return;
        await activity.AddAsync(new MedicineActivityChange
        {
            MedicineId = medicine.Id,
            Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime),
            Active = active,
            RecordedAt = now,
        }, cancellationToken);
    }
}
