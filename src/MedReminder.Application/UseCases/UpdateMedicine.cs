using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;

namespace MedReminder.Application.UseCases;

// Aggiorna campi non-schedule e non-stock. Cambi di dose/frequenza
// vanno canalizzati su ChangeMedicationSchedule per preservare la
// timeline; qui si aggiornano solo i campi "amministrativi" più,
// opzionalmente, gli slot di somministrazione.
//
// Semantica AdministrationSlots (Incremento 10):
//   null           → non toccare gli slot esistenti (il caller non
//                    intende modificarli in questa richiesta).
//   lista vuota    → azzera gli slot: la medicina torna al modello
//                    legacy dose × frequenza.
//   lista con item → sostituzione atomica (delete + insert) degli slot
//                    correnti.
public sealed record UpdateMedicineCommand(
    Guid MedicineId,
    string Name,
    string? ActiveIngredient,
    string? Package,
    string Unit,
    int ThresholdDays,
    NotificationChannels NotificationChannels,
    DateOnly? EndDate,
    string? DoctorName,
    string? Notes,
    bool IsActive,
    IReadOnlyList<AdministrationSlotInput>? AdministrationSlots = null);

public sealed class UpdateMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public UpdateMedicine(
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _slots = slots;
        _uow = uow;
        _clock = clock;
    }

    public async Task ExecuteAsync(UpdateMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("Il nome della medicina è obbligatorio.", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.Unit))
            throw new ArgumentException("L'unità di misura è obbligatoria.", nameof(cmd));
        if (cmd.ThresholdDays < 0)
            throw new ArgumentException("La soglia in giorni non può essere negativa.", nameof(cmd));

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        if (cmd.EndDate is { } end && end < medicine.StartDate)
            throw new ArgumentException("La data di fine terapia non può precedere quella di inizio.", nameof(cmd));

        medicine.Name = cmd.Name.Trim();
        medicine.ActiveIngredient = string.IsNullOrWhiteSpace(cmd.ActiveIngredient) ? null : cmd.ActiveIngredient.Trim();
        medicine.Package = string.IsNullOrWhiteSpace(cmd.Package) ? null : cmd.Package.Trim();
        medicine.Unit = cmd.Unit.Trim();
        medicine.ThresholdDays = cmd.ThresholdDays;
        medicine.NotificationChannels = cmd.NotificationChannels;
        medicine.EndDate = cmd.EndDate;
        medicine.DoctorName = string.IsNullOrWhiteSpace(cmd.DoctorName) ? null : cmd.DoctorName.Trim();
        medicine.Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim();
        medicine.IsActive = cmd.IsActive;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _medicines.UpdateAsync(medicine, cancellationToken);

        if (cmd.AdministrationSlots is not null)
        {
            await _slots.DeleteForMedicineAsync(medicine.Id, cancellationToken);
            if (cmd.AdministrationSlots.Count > 0)
            {
                await _slots.AddRangeAsync(BuildSlots(medicine.Id, cmd.AdministrationSlots), cancellationToken);
            }
        }

        await _uow.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<MedicationAdministrationSlot> BuildSlots(
        Guid medicineId, IReadOnlyList<AdministrationSlotInput> inputs)
    {
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            yield return new MedicationAdministrationSlot
            {
                MedicineId = medicineId,
                Dose = input.Dose,
                Time = input.Time,
                TimingLabel = string.IsNullOrWhiteSpace(input.TimingLabel) ? null : input.TimingLabel.Trim(),
                Order = i,
            };
        }
    }
}
