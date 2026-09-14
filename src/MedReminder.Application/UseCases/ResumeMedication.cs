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
            ?? throw new InvalidOperationException($"Medicina {cmd.MedicineId} non trovata.");

        var open = await _suspensions.GetOpenSuspensionAsync(cmd.MedicineId, cancellationToken)
            ?? throw new InvalidOperationException("La medicina non è sospesa.");

        if (cmd.EndDate < open.StartDate)
        {
            throw new ArgumentException(
                "La data di ripresa non può precedere l'inizio della sospensione.",
                nameof(cmd));
        }

        open.EndDate = cmd.EndDate;
        medicine.UpdatedAt = _clock.GetUtcNow();

        await _suspensions.UpdateAsync(open, cancellationToken);
        await _medicines.UpdateAsync(medicine, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);
    }
}
