using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.UseCases;

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
    IReadOnlyList<AdministrationSlotInput>? AdministrationSlots = null);

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
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _medicines.AddAsync(medicine, cancellationToken);

        // First entry of the versioned schedule.
        await _schedules.AddAsync(new MedicationScheduleHistory
        {
            MedicineId = medicine.Id,
            EffectiveFrom = cmd.StartDate,
            DosePerAdministration = cmd.DosePerAdministration,
            AdministrationsPerDay = cmd.AdministrationsPerDay,
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
        if (cmd.DosePerAdministration <= 0m)
            throw new ArgumentException("Dose per administration must be positive.", nameof(cmd));
        if (cmd.AdministrationsPerDay <= 0)
            throw new ArgumentException("Administrations per day must be at least 1.", nameof(cmd));
        if (cmd.ThresholdDays < 0)
            throw new ArgumentException("Threshold in days cannot be negative.", nameof(cmd));
        if (cmd.EndDate is { } end && end < cmd.StartDate)
            throw new ArgumentException("Therapy end date cannot precede start date.", nameof(cmd));
        if (cmd.InitialQuantity < 0m)
            throw new ArgumentException("Initial quantity cannot be negative.", nameof(cmd));
    }
}
