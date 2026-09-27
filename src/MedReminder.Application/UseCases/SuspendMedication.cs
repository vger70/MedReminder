using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
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
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SuspendMedication(
        IMedicineRepository medicines,
        IMedicationSuspensionRepository suspensions,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _suspensions = suspensions;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Guid> ExecuteAsync(SuspendMedicationCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return await WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    private async Task<Guid> ExecuteCoreAsync(SuspendMedicationCommand cmd, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var open = await _suspensions.GetOpenSuspensionAsync(cmd.MedicineId, cancellationToken);
        if (open is not null)
            throw new InvalidOperationException("Medicine is already suspended.");

        var suspension = new MedicationSuspension
        {
            MedicineId = medicine.Id,
            StartDate = cmd.StartDate,
            EndDate = null,
            Reason = string.IsNullOrWhiteSpace(cmd.Reason) ? null : cmd.Reason.Trim(),
            RecordedAt = _clock.GetUtcNow(),
        };

        medicine.UpdatedAt = _clock.GetUtcNow();
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _suspensions.AddAsync(suspension, cancellationToken);
        await _operations.AppendAsync([Operations.Suspension(suspension)], cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
        return suspension.Id;
    }
}
