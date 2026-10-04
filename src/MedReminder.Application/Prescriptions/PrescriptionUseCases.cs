using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Prescriptions;

// What the user enters for a prescription. Id null records a new one.
// Dispensations: null or 1 for a single prescription.
//
// The dispensations of a repeatable prescription are given as edits, not
// as the whole list, because another device may record or remove one
// while the editor is open: DispensationEdits holds the new ones (Id
// null) and the changed ones (Id set); RemovedDispensations the ids the
// user removed. A dispensation not named stays as it is.
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
    IReadOnlyList<DispensationEntry>? DispensationEdits = null,
    IReadOnlyCollection<Guid>? RemovedDispensations = null);

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
// prescription on another device, and an edit of the prescription never
// overrides a dispensation recorded elsewhere.
//
// Validation checks the dates of the dispensations added or changed, and
// of all of them when the issue date or "valid until" changes; the others
// are only counted. A single prescription ignores the dispensations a
// sync may have left on it (PrescriptionListQuery hides them too).
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
        var datesChanged = isNew
            || prescription.IssuedOn != cmd.IssuedOn
            || prescription.ValidUntil != cmd.ValidUntil
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

        var edits = cmd.DispensationEdits ?? [];
        var removedIds = cmd.RemovedDispensations ?? [];
        var existing = isNew ? [] : await _dispensations.ListForPrescriptionAsync(prescription.Id, ct);
        // Left by a sync on a single prescription: neither counted nor
        // touched, unless the user removes them explicitly.
        var held = prescription.IsRepeatable || edits.Count > 0
            ? existing
            : existing.Where(d => removedIds.Contains(d.Id)).ToList();
        var byId = held.ToDictionary(d => d.Id);

        var removed = held.Where(d => removedIds.Contains(d.Id)).ToList();
        var added = new List<PrescriptionDispensation>();
        var updated = new List<(PrescriptionDispensation Current, DispensationEntry Entry)>();
        foreach (var entry in edits)
        {
            if (entry.Id is null)
            {
                added.Add(new PrescriptionDispensation
                {
                    PrescriptionId = prescription.Id,
                    MedicineId = prescription.MedicineId,
                    CollectedOn = entry.CollectedOn,
                    Packages = entry.Packages,
                    RecordedAt = now,
                    UpdatedAt = now,
                });
                continue;
            }
            // Removed meanwhile on another device, or removed here too:
            // the change does not bring it back.
            if (!byId.TryGetValue(entry.Id.Value, out var current) || removedIds.Contains(current.Id)) continue;
            if (current.CollectedOn == entry.CollectedOn && current.Packages == entry.Packages) continue;
            updated.Add((current, entry));
        }

        var touched = added
            .Concat(updated.Select(u => new PrescriptionDispensation
            {
                Id = u.Current.Id,
                PrescriptionId = prescription.Id,
                MedicineId = prescription.MedicineId,
                CollectedOn = u.Entry.CollectedOn,
                Packages = u.Entry.Packages,
            }))
            .ToList();
        var untouched = held
            .Where(d => !removedIds.Contains(d.Id) && updated.All(u => u.Current.Id != d.Id))
            .ToList();
        var error = datesChanged
            ? PrescriptionRules.Validate(prescription, [.. touched, .. untouched])
            : PrescriptionRules.Validate(prescription, touched, untouched.Count);
        if (error is not null)
        {
            (prescription.RequestedOn, prescription.IssuedOn, prescription.Code, prescription.Packages,
                prescription.ValidUntil, prescription.CollectedOn, prescription.Dispensations) = before;
            throw new InvalidPrescriptionException(error.Value);
        }

        var ops = new List<SyncOperationBody>();
        if (changed)
        {
            prescription.UpdatedAt = now;
            if (isNew) await _prescriptions.AddAsync(prescription, ct);
            else await _prescriptions.UpdateAsync(prescription, ct);
            ops.Add(Operations.Prescription(prescription, deleted: false));
        }
        foreach (var listed in removed)
        {
            var row = await _dispensations.GetAsync(listed.Id, ct) ?? listed;
            row.UpdatedAt = now;
            await _dispensations.RemoveAsync(row, ct);
            ops.Add(Operations.Dispensation(row, deleted: true));
        }
        foreach (var (current, entry) in updated)
        {
            var row = await _dispensations.GetAsync(current.Id, ct) ?? current;
            row.CollectedOn = entry.CollectedOn;
            row.Packages = entry.Packages;
            row.UpdatedAt = now;
            await _dispensations.UpdateAsync(row, ct);
            ops.Add(Operations.Dispensation(row, deleted: false));
        }
        foreach (var dispensation in added)
        {
            await _dispensations.AddAsync(dispensation, ct);
            ops.Add(Operations.Dispensation(dispensation, deleted: false));
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
    private readonly SavePrescription _save;

    public RecordDispensation(IPrescriptionRepository prescriptions, SavePrescription save)
    {
        _prescriptions = prescriptions;
        _save = save;
    }

    public Task ExecuteAsync(Guid prescriptionId, DateOnly collectedOn, int? packages, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var p = await _prescriptions.GetAsync(prescriptionId, ct)
                ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found.");
            if (!p.IsRepeatable) throw new InvalidOperationException("Only a repeatable prescription has dispensations.");
            return await _save.ExecuteCoreAsync(new SavePrescriptionCommand(
                p.Id, p.MedicineId, p.RequestedOn, p.IssuedOn, p.Code, p.Packages, p.ValidUntil, p.CollectedOn,
                p.Dispensations, [new DispensationEntry(null, collectedOn, packages)]), ct);
        }, cancellationToken);
}

// Marks a prescription collected on a day, keeping the rest as it is.
// Offered after a new package of the same medicine.
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

// Deletes a prescription. Its dispensations are not deleted: they stay,
// unused (every reader ignores a dispensation without its prescription),
// so a concurrent edit on another device that wins the prescription's
// register brings it back with its dispensations. They go with the
// medicine (MedicineDeletionRepository).
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
