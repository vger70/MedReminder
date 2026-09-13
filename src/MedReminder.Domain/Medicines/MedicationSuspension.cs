namespace MedReminder.Domain.Medicines;

// Periodo di sospensione della terapia. EndDate = null indica una
// sospensione aperta (ancora in corso). Durante la sospensione:
//  - il ConsumptionMaterializer salta i giorni sospesi;
//  - RunOutForecast restituisce null (nessuna ETA);
//  - NotificationCycle non attiva avvisi.
public sealed class MedicationSuspension
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MedicineId { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; set; }

    public string? Reason { get; set; }
}
