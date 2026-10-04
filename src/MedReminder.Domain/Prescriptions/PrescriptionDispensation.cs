namespace MedReminder.Domain.Prescriptions;

// One collection at the pharmacy under a repeatable prescription
// (Prescription.Dispensations > 1). Replicated on its own, so that two
// devices recording a dispensation at the same time both keep theirs.
// An organizational record, never logged with the medicine name.
public sealed class PrescriptionDispensation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid PrescriptionId { get; init; }

    public required Guid MedicineId { get; init; }

    public DateOnly CollectedOn { get; set; }

    public int? Packages { get; set; }

    public DateTimeOffset RecordedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}
