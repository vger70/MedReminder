using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.UseCases;

public sealed record SuspendMedicationCommand(
    Guid MedicineId,
    DateOnly StartDate,
    string? Reason = null);

public sealed class SuspendMedication
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SuspendMedication(
        IMedicineRepository medicines,
        IMedicationSuspensionRepository suspensions,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _suspensions = suspensions;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Guid> ExecuteAsync(SuspendMedicationCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        var open = await _suspensions.GetOpenSuspensionAsync(cmd.MedicineId, cancellationToken);
        if (open is not null)
            throw new InvalidOperationException("La medicina è già sospesa.");

        var suspension = new MedicationSuspension
        {
            MedicineId = medicine.Id,
            StartDate = cmd.StartDate,
            EndDate = null,
            Reason = string.IsNullOrWhiteSpace(cmd.Reason) ? null : cmd.Reason.Trim(),
        };

        medicine.UpdatedAt = _clock.GetUtcNow();
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _suspensions.AddAsync(suspension, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return suspension.Id;
    }
}
