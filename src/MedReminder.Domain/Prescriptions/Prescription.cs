namespace MedReminder.Domain.Prescriptions;

// A prescription the user follows from the request to the pharmacy
// (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2): requested from the doctor,
// issued (with its code, number of packages and the date it lapses),
// collected at the pharmacy. Every step is optional, so a prescription
// can be recorded at any stage. An organizational record: no clinical
// content, and the code (the Italian NRE, for example) is never logged.
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

    public DateTimeOffset RecordedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public PrescriptionStatus StatusOn(DateOnly today)
    {
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
