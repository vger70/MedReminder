using MedReminder.Application.Abstractions;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Prescriptions;

// One prescription with the medicine it belongs to, for the list.
public sealed record PrescriptionListItem(
    Prescription Prescription,
    string MedicineName,
    bool MedicineActive,
    PrescriptionStatus Status);

// Read-only query behind Therapy → Prescriptions…: every prescription of
// the profile, the ones to act on first (to collect, requested, expired,
// collected), the most recent first within each group.
public sealed class PrescriptionListQuery
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IMedicineRepository _medicines;
    private readonly TimeProvider _clock;

    public PrescriptionListQuery(IPrescriptionRepository prescriptions, IMedicineRepository medicines, TimeProvider clock)
    {
        _prescriptions = prescriptions;
        _medicines = medicines;
        _clock = clock;
    }

    public DateOnly LocalToday()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public async Task<IReadOnlyList<PrescriptionListItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var medicines = (await _medicines.ListAllAsync(cancellationToken)).ToDictionary(m => m.Id);
        return (await _prescriptions.ListAllAsync(cancellationToken))
            .Where(p => medicines.ContainsKey(p.MedicineId))
            .Select(p => new PrescriptionListItem(
                p, medicines[p.MedicineId].Name, medicines[p.MedicineId].IsActive, p.StatusOn(today)))
            .OrderBy(i => i.Status)
            .ThenByDescending(i => i.Prescription.IssuedOn ?? i.Prescription.RequestedOn ?? i.Prescription.CollectedOn)
            .ThenBy(i => i.MedicineName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // The prescriptions of a medicine still to collect (issued, not
    // collected, expired included), oldest issue first: what a new
    // package of that medicine most likely came from.
    public async Task<IReadOnlyList<Prescription>> OpenForMedicineAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var today = LocalToday();
        return (await _prescriptions.ListForMedicineAsync(medicineId, cancellationToken))
            .Where(p => p.StatusOn(today) is PrescriptionStatus.ToCollect or PrescriptionStatus.Expired)
            .OrderBy(p => p.IssuedOn)
            .ToList();
    }
}
