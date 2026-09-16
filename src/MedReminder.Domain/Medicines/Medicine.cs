using MedReminder.Domain.Notifications;

namespace MedReminder.Domain.Medicines;

// Aggregate root: rappresenta una medicina gestita dall'utente.
//
// I setter pubblici permettono la deserializzazione EF Core; le regole di
// business che vincolano transizioni di stato (attivazione, cambio schedule,
// incremento di StockEpoch dopo un movimento positivo, ecc.) risiedono
// nella Application layer, non su questa classe. Le funzioni di calcolo
// derivato (residuo, ETA, notify?) sono in MedReminder.Domain.Calculations.
public sealed class Medicine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }

    public string? ActiveIngredient { get; set; }

    public string? Package { get; set; }

    // Codice unità come stringa libera (spec §4). Un elenco suggerito
    // ("compresse", "capsule", "ml", ...) è tenuto lato UI: qui non si
    // vincola per non ostacolare unità non previste.
    public required string Unit { get; set; }

    public decimal DosePerAdministration { get; set; }

    public int AdministrationsPerDay { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    // Soglia di avviso espressa in giorni residui stimati (spec §8).
    // 0 disabilita di fatto la notifica per esaurimento imminente.
    public int ThresholdDays { get; set; }

    public string? DoctorName { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    // Incrementato dalla Application ad ogni movimento di stock positivo
    // (spec §8: dopo un nuovo rifornimento il ciclo di avviso riparte).
    // Le NotificationEvent legate all'epoch precedente non bloccano più
    // il ciclo corrente.
    public int StockEpoch { get; set; } = 1;

    public NotificationChannels NotificationChannels { get; set; }
        = NotificationChannels.Windows;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
