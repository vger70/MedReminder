using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.UseCases;

// Change of dose, of intake frequency or of schedule shape. Adds a
// new record to MedicationScheduleHistory with EffectiveFrom = the
// change date; also updates the "current" fields on the Medicine
// entity for the UI. The schedule history (docs/ANALYSIS.md §2.3)
// lets the ConsumptionMaterializer apply the correct rate to each
// day.
//
// NewSchedule (A1, docs/ANALYSIS-A1-REGIMENS.md §3.5) is optional.
// When null the use case builds a FixedDailySchedule from
// NewDosePerAdministration and NewAdministrationsPerDay (existing
// behavior). When set it is persisted verbatim; the legacy fields
// become display-only summary values on the Medicine entity.
public sealed record ChangeMedicationScheduleCommand(
    Guid MedicineId,
    decimal NewDosePerAdministration,
    int NewAdministrationsPerDay,
    DateOnly EffectiveFrom,
    Schedule? NewSchedule = null);

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

        if (cmd.NewSchedule is null)
        {
            if (cmd.NewDosePerAdministration <= 0m)
                throw new ArgumentException("Dose per administration must be positive.", nameof(cmd));
            if (cmd.NewAdministrationsPerDay <= 0)
                throw new ArgumentException("Administrations per day must be at least 1.", nameof(cmd));
        }
        else
        {
            if (cmd.NewDosePerAdministration < 0m)
                throw new ArgumentException("Display dose cannot be negative.", nameof(cmd));
            if (cmd.NewAdministrationsPerDay < 0)
                throw new ArgumentException("Display administrations per day cannot be negative.", nameof(cmd));
        }

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        if (cmd.EffectiveFrom < medicine.StartDate)
        {
            throw new ArgumentException(
                "Effective date cannot precede the therapy start date.",
                nameof(cmd));
        }

        var effectiveSchedule = cmd.NewSchedule
            ?? new FixedDailySchedule(cmd.NewDosePerAdministration, cmd.NewAdministrationsPerDay);

        var (kind, payload) = ScheduleCodec.Serialize(effectiveSchedule);
        var entry = new MedicationScheduleHistory
        {
            MedicineId = medicine.Id,
            EffectiveFrom = cmd.EffectiveFrom,
            DosePerAdministration = cmd.NewDosePerAdministration,
            AdministrationsPerDay = cmd.NewAdministrationsPerDay,
            ScheduleKind = kind,
            SchedulePayload = payload,
        };

        medicine.DosePerAdministration = cmd.NewDosePerAdministration;
        medicine.AdministrationsPerDay = cmd.NewAdministrationsPerDay;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _schedules.AddAsync(entry, cancellationToken);
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
