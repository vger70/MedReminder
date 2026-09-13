namespace MedReminder.Domain.Notifications;

// Registrazione di una notifica emessa (o tentata). Usata da
// NotificationCycle per non ri-inviare la stessa notifica finché
// l'epoch della medicina non cambia (spec §8, docs/ANALYSIS.md §2.9).
public sealed class NotificationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required int StockEpoch { get; init; }

    public required DateTimeOffset TriggeredAt { get; init; }

    public required NotificationChannels Channel { get; init; }

    public required int DaysRemainingAtSend { get; init; }

    public required bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}
