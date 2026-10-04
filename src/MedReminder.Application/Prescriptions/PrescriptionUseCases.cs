using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Prescriptions;

// What the user enters for a prescription. Id null records a new one.
// Dispensations: null or 1 for a single prescription. DispensationRecords:
// the dispensations the prescription holds once saved (a repeatable one
// only), null to keep those recorded.
public sealed record SavePrescriptionCommand(
    Guid? Id,
    Guid MedicineId,
    DateOnly? RequestedOn,
    DateOnly? IssuedOn,
    string? Code,
    int? Packages,
    DateOnly? ValidUntil,
    DateOnly? CollectedOn,
    int? Dispensations = null,
    IReadOnlyList<DispensationEntry>? DispensationRecords = null);

// One dispensation as entered. Id null records a new one.
public sealed record DispensationEntry(Guid? Id, DateOnly CollectedOn, int? Packages);

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
// §3.2) and the dispensations of a repeatable one. The whole prescription
// is one replicated register (PrescriptionChanged), written with its full
// state when it is new or one of its fields changed; each dispensation is
// its own register (DispensationChanged), written only when it is added,
// changed or removed. An unchanged register is not written again, so a
// dispensation recorded here never overrides a concurrent edit of the
// prescription on another device.
public sealed class SavePrescription
{
    private readonly IMedicineRepository _medicines;
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IPrescriptionDispensationRepository _dispensations;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public SavePrescription(
        IMedicineRepository medicines,
        IPrescriptionRepository prescriptions,
        IPrescriptionDispensationRepository dispensations,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _medicines = medicines;
        _prescriptions = prescriptions;
        _dispensations = dispensations;
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

        var code = string.IsNullOrWhiteSpace(cmd.Code) ? null : cmd.Code.Trim();
        var changed = isNew
            || prescription.RequestedOn != cmd.RequestedOn
            || prescription.IssuedOn != cmd.IssuedOn
            || prescription.Code != code
            || prescription.Packages != cmd.Packages
            || prescription.ValidUntil != cmd.ValidUntil
            || prescription.CollectedOn != cmd.CollectedOn
            || prescription.Dispensations != cmd.Dispensations;
        // Restored on a validation failure: the instance is tracked.
        var before = (prescription.RequestedOn, prescription.IssuedOn, prescription.Code, prescription.Packages,
            prescription.ValidUntil, prescription.CollectedOn, prescription.Dispensations);
        prescription.RequestedOn = cmd.RequestedOn;
        prescription.IssuedOn = cmd.IssuedOn;
        prescription.Code = code;
        prescription.Packages = cmd.Packages;
        prescription.ValidUntil = cmd.ValidUntil;
        prescription.CollectedOn = cmd.CollectedOn;
        prescription.Dispensations = cmd.Dispensations;

        var existing = isNew ? [] : await _dispensations.ListForPrescriptionAsync(prescription.Id, ct);
        var wanted = cmd.DispensationRecords ?? [.. existing.Select(d => new DispensationEntry(d.Id, d.CollectedOn, d.Packages))];
        var candidates = wanted.Select(e => new PrescriptionDispensation
        {
            Id = e.Id ?? Guid.NewGuid(),
            PrescriptionId = prescription.Id,
            MedicineId = prescription.MedicineId,
            CollectedOn = e.CollectedOn,
            Packages = e.Packages,
            RecordedAt = now,
            UpdatedAt = now,
        }).ToList();
        if (PrescriptionRules.Validate(prescription, candidates) is { } error)
        {
            (prescription.RequestedOn, prescription.IssuedOn, prescription.Code, prescription.Packages,
                prescription.ValidUntil, prescription.CollectedOn, prescription.Dispensations) = before;
            throw new InvalidPrescriptionException(error);
        }

        var ops = new List<SyncOperationBody>();
        if (changed)
        {
            prescription.UpdatedAt = now;
            if (isNew) await _prescriptions.AddAsync(prescription, ct);
            else await _prescriptions.UpdateAsync(prescription, ct);
            ops.Add(Operations.Prescription(prescription, deleted: false));
        }
        var byId = existing.ToDictionary(d => d.Id);
        foreach (var removed in existing.Where(d => candidates.All(c => c.Id != d.Id)))
        {
            var row = await _dispensations.GetAsync(removed.Id, ct) ?? removed;
            row.UpdatedAt = now;
            await _dispensations.RemoveAsync(row, ct);
            ops.Add(Operations.Dispensation(row, deleted: true));
        }
        foreach (var candidate in candidates)
        {
            if (!byId.TryGetValue(candidate.Id, out var current))
            {
                await _dispensations.AddAsync(candidate, ct);
                ops.Add(Operations.Dispensation(candidate, deleted: false));
                continue;
            }
            if (current.CollectedOn == candidate.CollectedOn && current.Packages == candidate.Packages) continue;
            var row = await _dispensations.GetAsync(current.Id, ct) ?? current;
            row.CollectedOn = candidate.CollectedOn;
            row.Packages = candidate.Packages;
            row.UpdatedAt = now;
            await _dispensations.UpdateAsync(row, ct);
            ops.Add(Operations.Dispensation(row, deleted: false));
        }
        if (ops.Count > 0)
        {
            await _operations.AppendAsync(ops, ct);
            await _uow.SaveChangesAsync(ct);
        }
        return prescription.Id;
    }
}

// Records one dispensation of a repeatable prescription, keeping the
// rest as it is. Offered after a new package of the same medicine.
public sealed class RecordDispensation
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IPrescriptionDispensationRepository _dispensations;
    private readonly SavePrescription _save;

    public RecordDispensation(
        IPrescriptionRepository prescriptions,
        IPrescriptionDispensationRepository dispensations,
        SavePrescription save)
    {
        _prescriptions = prescriptions;
        _dispensations = dispensations;
        _save = save;
    }

    public Task ExecuteAsync(Guid prescriptionId, DateOnly collectedOn, int? packages, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var p = await _prescriptions.GetAsync(prescriptionId, ct)
                ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found.");
            if (!p.IsRepeatable) throw new InvalidOperationException("Only a repeatable prescription has dispensations.");
            var records = (await _dispensations.ListForPrescriptionAsync(p.Id, ct))
                .Select(d => new DispensationEntry(d.Id, d.CollectedOn, d.Packages))
                .Append(new DispensationEntry(null, collectedOn, packages))
                .ToList();
            return await _save.ExecuteCoreAsync(new SavePrescriptionCommand(
                p.Id, p.MedicineId, p.RequestedOn, p.IssuedOn, p.Code, p.Packages, p.ValidUntil, p.CollectedOn,
                p.Dispensations, records), ct);
        }, cancellationToken);
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
                p.Id, p.MedicineId, p.RequestedOn, p.IssuedOn, p.Code, p.Packages, p.ValidUntil, collectedOn,
                p.Dispensations), ct);
        }, cancellationToken);
}

// Deletes a prescription and its dispensations, each with its tombstone.
public sealed class DeletePrescription
{
    private readonly IPrescriptionRepository _prescriptions;
    private readonly IPrescriptionDispensationRepository _dispensations;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public DeletePrescription(
        IPrescriptionRepository prescriptions,
        IPrescriptionDispensationRepository dispensations,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _prescriptions = prescriptions;
        _dispensations = dispensations;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task ExecuteAsync(Guid prescriptionId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var prescription = await _prescriptions.GetAsync(prescriptionId, ct);
            if (prescription is null) return true;
            var now = _clock.GetUtcNow();
            prescription.UpdatedAt = now;
            var ops = new List<SyncOperationBody>();
            foreach (var listed in await _dispensations.ListForPrescriptionAsync(prescription.Id, ct))
            {
                var dispensation = await _dispensations.GetAsync(listed.Id, ct) ?? listed;
                dispensation.UpdatedAt = now;
                await _dispensations.RemoveAsync(dispensation, ct);
                ops.Add(Operations.Dispensation(dispensation, deleted: true));
            }
            await _prescriptions.RemoveAsync(prescription, ct);
            ops.Insert(0, Operations.Prescription(prescription, deleted: true));
            await _operations.AppendAsync(ops, ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
}
