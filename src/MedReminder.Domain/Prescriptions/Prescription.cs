namespace MedReminder.Domain.Prescriptions;

// A prescription the user follows from the request to the pharmacy
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2): requested from the doctor,
// issued (with its code, number of packages and the date it lapses),
// collected at the pharmacy. Every step is optional, so a prescription
// can be recorded at any stage. An organizational record: no clinical
// content, and the code (the Italian NRE, for example) is never logged.
//
// A repeatable prescription (Dispensations > 1) covers several
// dispensations at the pharmacy within its validity; each one is a
// PrescriptionDispensation and CollectedOn stays empty. Null or 1 is a
// single prescription, collected once.
public sealed class Prescription
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public DateOnly? RequestedOn { get; set; }

    public DateOnly? IssuedOn { get; set; }

    // Prescription code as printed on the slip; free text.
    public string? Code { get; set; }

    public int? Packages { get; set; }

    // Last day the pharmacy accepts it.
    public DateOnly? ValidUntil { get; set; }

    public DateOnly? CollectedOn { get; set; }

    // Number of dispensations the prescription allows; null or 1 for a
    // single prescription.
    public int? Dispensations { get; set; }

    public bool IsRepeatable => Dispensations > 1;

    public DateTimeOffset RecordedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    // `dispensationsCollected` is the number of dispensations recorded,
    // read only for a repeatable prescription: to collect while some are
    // left and the validity lasts, collected once all are, expired when
    // the validity ended with some left.
    public PrescriptionStatus StatusOn(DateOnly today, int dispensationsCollected = 0)
    {
        if (IsRepeatable)
        {
            if (dispensationsCollected >= Dispensations) return PrescriptionStatus.Collected;
            if (IssuedOn is null && dispensationsCollected == 0) return PrescriptionStatus.Requested;
            return ValidUntil is { } last && today > last
                ? PrescriptionStatus.Expired
                : PrescriptionStatus.ToCollect;
        }
        if (CollectedOn is not null) return PrescriptionStatus.Collected;
        if (IssuedOn is null) return PrescriptionStatus.Requested;
        return ValidUntil is { } until && today > until
            ? PrescriptionStatus.Expired
            : PrescriptionStatus.ToCollect;
    }
}

// Order matters: lists show the prescriptions to act on first.
public enum PrescriptionStatus
{
    ToCollect,
    Requested,
    Expired,
    Collected,
}
