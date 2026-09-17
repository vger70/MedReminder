namespace MedReminder.Domain.Medicines;

// Therapy suspension period. EndDate = null means an open suspension
// (still ongoing). While suspended:
//  - the ConsumptionMaterializer skips suspended days;
//  - RunOutForecast returns null (no ETA);
//  - NotificationCycle does not raise warnings.
public sealed class MedicationSuspension
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; set; }

    public string? Reason { get; set; }
}
