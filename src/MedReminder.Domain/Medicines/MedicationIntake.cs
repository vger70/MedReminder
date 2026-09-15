namespace MedReminder.Domain.Medicines;

// Registrazione di una singola assunzione (spec §6). Ora esposta via UI
// in Incremento 9: l'utente può marcare una dose come assunta, saltata
// o annullata; se Taken, la Application crea anche uno StockMovement
// Consumption per scalare la scorta. Day è la giornata a cui l'assunzione
// si riferisce (in fuso locale), non l'istante di registrazione — serve
// al ConsumptionCatchUp per non generare consumi automatici duplicati
// nei giorni in cui l'utente ha già registrato manualmente.
public sealed class MedicationIntake
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    // Giornata a cui si riferisce l'assunzione (fuso locale).
    public required DateOnly Day { get; init; }

    public DateTimeOffset? ScheduledAt { get; init; }

    public DateTimeOffset? ActualAt { get; set; }

    public required decimal Quantity { get; init; }

    public required IntakeStatus Status { get; set; }

    public string? Notes { get; set; }
}
