using MedReminder.Application.Abstractions;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Prescriptions;

// One prescription with the medicine it belongs to, for the list.
// Dispensations: those recorded, oldest first (a repeatable prescription
// only).
public sealed record PrescriptionListItem(
    Prescription Prescription,
    string MedicineName,
    bool MedicineActive,
    PrescriptionStatus Status,
    IReadOnlyList<PrescriptionDispensation> Dispensations)
{
    public int DispensationsLeft => PrescriptionRules.DispensationsLeft(Prescription, Dispensations);
}

// A prescription of a medicine still to collect, with its dispensations.
public sealed record OpenPrescription(Prescription Prescription, IReadOnlyList<PrescriptionDispensation> Dispensations)
{
    public int DispensationsLeft => PrescriptionRules.DispensationsLeft(Prescription, Dispensations);
}

// Read-only query behind Therapy → Prescriptions…: every prescription of
// the profile, the ones to act on first (to collect, requested, expired,
// collected), the most recent first within each group.
public sealed class PrescriptionListQuery
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IPrescriptionDispensationRepository _dispensations;
    private readonly IMedicineRepository _medicines;
    private readonly TimeProvider _clock;

    public PrescriptionListQuery(
        IPrescriptionRepository prescriptions,
        IPrescriptionDispensationRepository dispensations,
        IMedicineRepository medicines,
        TimeProvider clock)
    {
        _prescriptions = prescriptions;
        _dispensations = dispensations;
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
        var dispensations = Group(await _dispensations.ListAllAsync(cancellationToken));
        return (await _prescriptions.ListAllAsync(cancellationToken))
            .Where(p => medicines.ContainsKey(p.MedicineId))
            .Select(p => (p, d: Of(p, dispensations)))
            .Select(x => new PrescriptionListItem(
                x.p, medicines[x.p.MedicineId].Name, medicines[x.p.MedicineId].IsActive,
                x.p.StatusOn(today, x.d.Count), x.d))
            .OrderBy(i => i.Status)
            .ThenByDescending(i => i.Prescription.IssuedOn ?? i.Prescription.RequestedOn ?? i.Prescription.CollectedOn)
            .ThenBy(i => i.MedicineName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // The prescriptions of a medicine still to collect (issued, not
    // collected, expired included), oldest issue first: what a new
    // package of that medicine most likely came from. A repeatable
    // prescription only while it can take a dispensation (to collect):
    // one recorded after "valid until" would be rejected.
    public async Task<IReadOnlyList<OpenPrescription>> OpenForMedicineAsync(
        Guid medicineId, CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var dispensations = Group(await _dispensations.ListForMedicineAsync(medicineId, cancellationToken));
        return (await _prescriptions.ListForMedicineAsync(medicineId, cancellationToken))
            .Select(p => new OpenPrescription(p, Of(p, dispensations)))
            .Where(o => o.Prescription.StatusOn(today, o.Dispensations.Count) switch
            {
                PrescriptionStatus.ToCollect => true,
                PrescriptionStatus.Expired => !o.Prescription.IsRepeatable,
                _ => false,
            })
            .OrderBy(o => o.Prescription.IssuedOn)
            .ToList();
    }

    private static Dictionary<Guid, List<PrescriptionDispensation>> Group(IEnumerable<PrescriptionDispensation> all)
        => all.GroupBy(d => d.PrescriptionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.CollectedOn).ThenBy(d => d.RecordedAt).ToList());

    // A single prescription counts no dispensation, whatever a sync left.
    private static IReadOnlyList<PrescriptionDispensation> Of(
        Prescription p, Dictionary<Guid, List<PrescriptionDispensation>> grouped)
        => p.IsRepeatable && grouped.TryGetValue(p.Id, out var list) ? list : [];
}
