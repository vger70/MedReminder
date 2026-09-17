using MedReminder.Application.Abstractions;

namespace MedReminder.Application.UseCases;

public sealed record ResumeMedicationCommand(Guid MedicineId, DateOnly EndDate);

public sealed class ResumeMedication
{
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ResumeMedication(
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

    public async Task ExecuteAsync(ResumeMedicationCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var medicine = await _medicines.GetAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");

        var open = await _suspensions.GetOpenSuspensionAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException("Medicine is not suspended.");

        if (cmd.EndDate < open.StartDate)
        {
            throw new ArgumentException(
                "Resume date cannot precede the start of the suspension.",
                nameof(cmd));
        }

        open.EndDate = cmd.EndDate;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _suspensions.UpdateAsync(open, cancellationToken);
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
