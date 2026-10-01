using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Prescriptions;

namespace MedReminder.Application.Prescriptions;

// What the user enters for a prescription. Id null records a new one.
public sealed record SavePrescriptionCommand(
    Guid? Id,
    Guid MedicineId,
    DateOnly? RequestedOn,
    DateOnly? IssuedOn,
    string? Code,
    int? Packages,
    DateOnly? ValidUntil,
    DateOnly? CollectedOn);

public sealed class InvalidPrescriptionException : Exception
{
    public InvalidPrescriptionException(PrescriptionError error)
        : base($"The prescription is not valid: {error}.")
    {
        Error = error;
    }

    public PrescriptionError Error { get; }
}

// Records or changes a prescription (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.2). The whole prescription is one replicated register
// (PrescriptionChanged), so every save writes its full state.
public sealed class SavePrescription
{
    private readonly IMedicineRepository _medicines;
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SavePrescription(
        IMedicineRepository medicines,
        IPrescriptionRepository prescriptions,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _prescriptions = prescriptions;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task<Guid> ExecuteAsync(SavePrescriptionCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return WriteGate.RunExclusiveAsync(ct => ExecuteCoreAsync(cmd, ct), cancellationToken);
    }

    // Callers hold WriteGate.
    internal async Task<Guid> ExecuteCoreAsync(SavePrescriptionCommand cmd, CancellationToken ct)
    {
        _ = await _medicines.GetAsync(cmd.MedicineId, ct)
            ?? throw new InvalidOperationException($"Medicine {cmd.MedicineId} not found.");
        var now = _clock.GetUtcNow();

        Prescription prescription;
        var isNew = cmd.Id is null;
        if (isNew)
        {
            prescription = new Prescription { MedicineId = cmd.MedicineId, RecordedAt = now };
        }
        else
        {
            prescription = await _prescriptions.GetAsync(cmd.Id!.Value, ct)
                ?? throw new InvalidOperationException($"Prescription {cmd.Id} not found.");
            if (prescription.MedicineId != cmd.MedicineId)
                throw new InvalidOperationException("A prescription cannot move to another medicine.");
        }

        prescription.RequestedOn = cmd.RequestedOn;
        prescription.IssuedOn = cmd.IssuedOn;
        prescription.Code = string.IsNullOrWhiteSpace(cmd.Code) ? null : cmd.Code.Trim();
        prescription.Packages = cmd.Packages;
        prescription.ValidUntil = cmd.ValidUntil;
        prescription.CollectedOn = cmd.CollectedOn;
        prescription.UpdatedAt = now;
        if (PrescriptionRules.Validate(prescription) is { } error) throw new InvalidPrescriptionException(error);

        if (isNew) await _prescriptions.AddAsync(prescription, ct);
        else await _prescriptions.UpdateAsync(prescription, ct);
        await _operations.AppendAsync([Operations.Prescription(prescription, deleted: false)], ct);
        await _uow.SaveChangesAsync(ct);
        return prescription.Id;
    }
}

// Marks a prescription collected on a day, keeping the rest as it is.
// Offered after a new package of the same medicine is recorded.
public sealed class CollectPrescription
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly SavePrescription _save;

    public CollectPrescription(IPrescriptionRepository prescriptions, SavePrescription save)
    {
        _prescriptions = prescriptions;
        _save = save;
    }

    public Task ExecuteAsync(Guid prescriptionId, DateOnly collectedOn, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var p = await _prescriptions.GetAsync(prescriptionId, ct)
                ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found.");
            return await _save.ExecuteCoreAsync(new SavePrescriptionCommand(
                p.Id, p.MedicineId, p.RequestedOn, p.IssuedOn, p.Code, p.Packages, p.ValidUntil, collectedOn), ct);
        }, cancellationToken);
}

public sealed class DeletePrescription
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeletePrescription(
        IPrescriptionRepository prescriptions,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _prescriptions = prescriptions;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task ExecuteAsync(Guid prescriptionId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var prescription = await _prescriptions.GetAsync(prescriptionId, ct);
            if (prescription is null) return true;
            prescription.UpdatedAt = _clock.GetUtcNow();
            await _prescriptions.RemoveAsync(prescription, ct);
            await _operations.AppendAsync([Operations.Prescription(prescription, deleted: true)], ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
}
