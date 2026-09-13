namespace MedReminder.Domain.Medicines;

// Registrazione di una singola assunzione (spec §6). Presente nel modello
// perché richiesto dalle specifiche ma NON usata dal calcolo di residuo
// nell'MVP: il consumo giornaliero è derivato dallo schema configurato,
// come esplicitato in docs/ANALYSIS.md §1.1 punto 1.
public sealed class MedicationIntake
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public DateTimeOffset? ScheduledAt { get; init; }

    public DateTimeOffset? ActualAt { get; set; }

    public required decimal Quantity { get; init; }

    public required IntakeStatus Status { get; set; }

    public string? Notes { get; set; }
}
