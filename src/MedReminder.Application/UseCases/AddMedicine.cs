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
    decimal InitialQuantity = 0m);

public sealed class AddMedicine
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IStockMovementRepository _stock;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AddMedicine(
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IStockMovementRepository stock,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _schedules = schedules;
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

        // Prima entry della schedule versionata.
        await _schedules.AddAsync(new MedicationScheduleHistory
        {
            MedicineId = medicine.Id,
            EffectiveFrom = cmd.StartDate,
            DosePerAdministration = cmd.DosePerAdministration,
            AdministrationsPerDay = cmd.AdministrationsPerDay,
        }, cancellationToken);

        // Carico iniziale del magazzino (se >0).
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

        await _uow.SaveChangesAsync(cancellationToken);
        return medicine.Id;
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
            throw new ArgumentException("Il nome della medicina è obbligatorio.", nameof(cmd));
        if (string.IsNullOrWhiteSpace(cmd.Unit))
            throw new ArgumentException("L'unità di misura è obbligatoria.", nameof(cmd));
        if (cmd.DosePerAdministration <= 0m)
            throw new ArgumentException("La dose per somministrazione deve essere positiva.", nameof(cmd));
        if (cmd.AdministrationsPerDay <= 0)
            throw new ArgumentException("Le somministrazioni giornaliere devono essere almeno 1.", nameof(cmd));
        if (cmd.ThresholdDays < 0)
            throw new ArgumentException("La soglia in giorni non può essere negativa.", nameof(cmd));
        if (cmd.EndDate is { } end && end < cmd.StartDate)
            throw new ArgumentException("La data di fine terapia non può precedere quella di inizio.", nameof(cmd));
        if (cmd.InitialQuantity < 0m)
            throw new ArgumentException("La quantità iniziale non può essere negativa.", nameof(cmd));
    }
}
