namespace MedReminder.Domain.Medicines;

// Record of a single intake (spec §6). Exposed via UI in Increment 9:
// the user can mark a dose as Taken, Skipped or Cancelled; if Taken,
// the Application also creates a Consumption StockMovement to decrease
// the stock. Day is the calendar day the intake refers to (in local
// time), not the moment it was recorded — it lets ConsumptionCatchUp
// avoid generating duplicate automatic consumptions for the days the
// user has already recorded manually.
public sealed class MedicationIntake
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Calendar day this intake refers to (local zone).
    public required DateOnly Day { get; init; }

    public DateTimeOffset? ScheduledAt { get; init; }

    public DateTimeOffset? ActualAt { get; set; }

    public required decimal Quantity { get; init; }

    public required IntakeStatus Status { get; set; }

    public string? Notes { get; set; }
}
