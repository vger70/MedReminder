using MedReminder.Application.Abstractions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

// The three catalogue-linkage fields (NationalCode, AtcCode,
// LinkedReferenceMedicineId) are optional. Populate them when the
// UI has picked a row from the reference catalogue; leave them null
// for free-text entries. See ANALYSIS-DRUG-CATALOGUE.md §2.5.
//
// InitialSchedule (A1, docs/ANALYSIS-A1-REGIMENS.md §3.5) is
// optional. When null the use case builds a FixedDailySchedule
// from DosePerAdministration and AdministrationsPerDay (the legacy
// behavior). When set it is persisted verbatim and the legacy
// fields become read-only display values on the Medicine entity.
public sealed record AddMedicineCommand(
    string Name,
    string Unit,
    decimal DosePerAdministration,
    int AdministrationsPerDay,
    DateOnly StartDate,
    int ThresholdDays,
    NotificationChannels NotificationChannels,
    string? ActiveIngredient = null,
    string? Package = null,
    DateOnly? EndDate = null,
    string? DoctorName = null,
    string? Notes = null,
    decimal InitialQuantity = 0m,
    IReadOnlyList<AdministrationSlotInput>? AdministrationSlots = null,
    string? NationalCode = null,
    AtcCode? AtcCode = null,
    Guid? LinkedReferenceMedicineId = null,
    Schedule? InitialSchedule = null,
    bool RemindOnDose = false);

public sealed class AddMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AddMedicine(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationAdministrationSlotRepository slots,
        IStockMovementRepository stock,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
        _slots = slots;
        _stock = stock;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Guid> ExecuteAsync(AddMedicineCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        Validate(cmd);

        var effectiveSchedule = cmd.InitialSchedule
            ?? new FixedDailySchedule(cmd.DosePerAdministration, cmd.AdministrationsPerDay);

        var now = _clock.GetUtcNow();
        var medicine = new Medicine
        {
            Name = cmd.Name.Trim(),
            ActiveIngredient = string.IsNullOrWhiteSpace(cmd.ActiveIngredient) ? null : cmd.ActiveIngredient.Trim(),
            Package = string.IsNullOrWhiteSpace(cmd.Package) ? null : cmd.Package.Trim(),
            Unit = cmd.Unit.Trim(),
            DosePerAdministration = cmd.DosePerAdministration,
            AdministrationsPerDay = cmd.AdministrationsPerDay,
            StartDate = cmd.StartDate,
            EndDate = cmd.EndDate,
            ThresholdDays = cmd.ThresholdDays,
            DoctorName = string.IsNullOrWhiteSpace(cmd.DoctorName) ? null : cmd.DoctorName.Trim(),
            Notes = string.IsNullOrWhiteSpace(cmd.Notes) ? null : cmd.Notes.Trim(),
            IsActive = true,
            StockEpoch = 1,
            NotificationChannels = cmd.NotificationChannels,
            RemindOnDose = cmd.RemindOnDose,
            NationalCode = string.IsNullOrWhiteSpace(cmd.NationalCode) ? null : cmd.NationalCode.Trim(),
            AtcCode = cmd.AtcCode,
            LinkedReferenceMedicineId = cmd.LinkedReferenceMedicineId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _medicines.AddAsync(medicine, cancellationToken);

        // First entry of the versioned schedule. The codec carries
        // the schedule shape; the legacy dose / administrations
        // columns are used by the codec as the FixedDaily fallback
        // and, for other kinds, as display-only values.
        var (kind, payload) = ScheduleCodec.Serialize(effectiveSchedule);
        await _schedules.AddAsync(new MedicationScheduleHistory
        {
            MedicineId = medicine.Id,
            EffectiveFrom = cmd.StartDate,
            DosePerAdministration = cmd.DosePerAdministration,
            AdministrationsPerDay = cmd.AdministrationsPerDay,
            ScheduleKind = kind,
            SchedulePayload = payload,
        }, cancellationToken);

        // Initial stock load (if > 0).
        if (cmd.InitialQuantity > 0m)
        {
            var initialAt = ToLocalMiddayOffset(cmd.StartDate);
            await _stock.AddAsync(new StockMovement
            {
                MedicineId = medicine.Id,
                OccurredAt = initialAt,
                Kind = StockMovementKind.InitialLoad,
                QuantityDelta = cmd.InitialQuantity,
                StockEpoch = 1,
            }, cancellationToken);
        }

        // Optional administration slots (Increment 10). If provided
        // they are materialized; if null / empty the medicine stays on
        // the legacy dose × frequency model.
        if (cmd.AdministrationSlots is { Count: > 0 } slots)
        {
            await _slots.AddRangeAsync(BuildSlots(medicine.Id, slots), cancellationToken);
        }

        await _uow.SaveChangesAsync(cancellationToken);
        return medicine.Id;
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

    private DateTimeOffset ToLocalMiddayOffset(DateOnly day)
    {
        var local = day.ToDateTime(new TimeOnly(12, 0));
        var offset = _clock.LocalTimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static void Validate(AddMedicineCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("Medicine name is required.", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.Unit))
            throw new ArgumentException("Unit of measure is required.", nameof(cmd));
        if (cmd.InitialSchedule is null)
        {
            // Legacy path: the schedule is derived from Dose + Admin;
            // the existing invariants stay strict.
            if (cmd.DosePerAdministration <= 0m)
                throw new ArgumentException("Dose per administration must be positive.", nameof(cmd));
            if (cmd.AdministrationsPerDay <= 0)
                throw new ArgumentException("Administrations per day must be at least 1.", nameof(cmd));
        }
        else
        {
            // A1 path: the schedule itself has validated its own
            // invariants at construction. The legacy fields are
            // display-only (see §3.5 of ANALYSIS-A1-REGIMENS.md);
            // only refuse negative values so the Medicine summary
            // never carries garbage.
            if (cmd.DosePerAdministration < 0m)
                throw new ArgumentException("Display dose cannot be negative.", nameof(cmd));
            if (cmd.AdministrationsPerDay < 0)
                throw new ArgumentException("Display administrations per day cannot be negative.", nameof(cmd));
        }
        if (cmd.ThresholdDays < 0)
            throw new ArgumentException("Threshold in days cannot be negative.", nameof(cmd));
        if (cmd.EndDate is { } end && end < cmd.StartDate)
            throw new ArgumentException("Therapy end date cannot precede start date.", nameof(cmd));
        if (cmd.InitialQuantity < 0m)
            throw new ArgumentException("Initial quantity cannot be negative.", nameof(cmd));
    }
}
