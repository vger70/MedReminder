using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.UseCases;

// Cambio di dose o frequenza di assunzione. Aggiunge un nuovo record in
// MedicationScheduleHistory con EffectiveFrom = data di decorrenza;
// aggiorna anche i campi "correnti" sull'entità Medicine per la UI.
// La storia della schedule (docs/ANALYSIS.md §2.3) permette al
// ConsumptionMaterializer di applicare la dose corretta a ciascun giorno.
public sealed record ChangeMedicationScheduleCommand(
    Guid MedicineId,
    decimal NewDosePerAdministration,
    int NewAdministrationsPerDay,
    DateOnly EffectiveFrom);

public sealed class ChangeMedicationSchedule
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ChangeMedicationSchedule(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
        _uow = uow;
        _clock = clock;
    }

    public async Task ExecuteAsync(ChangeMedicationScheduleCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.NewDosePerAdministration <= 0m)
            throw new ArgumentException("La dose per somministrazione deve essere positiva.", nameof(cmd));
        if (cmd.NewAdministrationsPerDay <= 0)
            throw new ArgumentException("Le somministrazioni giornaliere devono essere almeno 1.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        if (cmd.EffectiveFrom < medicine.StartDate)
        {
            throw new ArgumentException(
                "La data di decorrenza non può precedere l'inizio della terapia.",
                nameof(cmd));
        }

        var entry = new MedicationScheduleHistory
        {
            MedicineId = medicine.Id,
            EffectiveFrom = cmd.EffectiveFrom,
            DosePerAdministration = cmd.NewDosePerAdministration,
            AdministrationsPerDay = cmd.NewAdministrationsPerDay,
        };

        medicine.DosePerAdministration = cmd.NewDosePerAdministration;
        medicine.AdministrationsPerDay = cmd.NewAdministrationsPerDay;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _schedules.AddAsync(entry, cancellationToken);
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
