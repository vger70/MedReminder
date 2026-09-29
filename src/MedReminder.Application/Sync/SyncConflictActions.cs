using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

public sealed record SyncConflictItem(SyncConflict Conflict, string MedicineName)
{
    // Only a medicine field can be restored with one tap (§4.5); the
    // other kinds are shown for review.
    public bool CanRestore => Conflict.Kind == SyncConflictKind.MedicineField && Conflict.Register is not null;
}

// The conflict review (B.1 Phase 3d, D7: the §4.5 list,
// docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §4.5). Read-only.
public sealed class SyncConflictsQuery
{
    private readonly ISyncConflictRepository _conflicts;
    private readonly IMedicineRepository _medicines;

    public SyncConflictsQuery(ISyncConflictRepository conflicts, IMedicineRepository medicines)
    {
        _conflicts = conflicts;
        _medicines = medicines;
    }

    public async Task<IReadOnlyList<SyncConflictItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var names = (await _medicines.ListAllAsync(cancellationToken)).ToDictionary(m => m.Id, m => m.Name);
        return [.. (await _conflicts.ListAllAsync(cancellationToken))
            .OrderByDescending(c => c.DetectedAt)
            .Select(c => new SyncConflictItem(c, names.GetValueOrDefault(c.MedicineId, string.Empty)))];
    }
}

// "Restore": writes the losing value of a medicine field again, as a new
// edit. It becomes the newest version on every device, and since its
// writer has seen both versions the conflict is cleared everywhere.
public sealed class RestoreSyncConflict
{
    private readonly ISyncConflictRepository _conflicts;
    private readonly IMedicineRepository _medicines;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public RestoreSyncConflict(
        ISyncConflictRepository conflicts,
        IMedicineRepository medicines,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _conflicts = conflicts;
        _medicines = medicines;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public Task ExecuteAsync(Guid conflictId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var conflict = (await _conflicts.ListAllAsync(ct)).FirstOrDefault(c => c.Id == conflictId)
                ?? throw new InvalidOperationException("The conflict is no longer listed.");
            if (conflict.Kind != SyncConflictKind.MedicineField || conflict.Register is not { } field)
                throw new InvalidOperationException("Only a medicine field can be restored.");

            var medicine = await _medicines.GetAsync(conflict.MedicineId, ct)
                ?? throw new InvalidOperationException($"Medicine {conflict.MedicineId} not found.");
            var before = MedicineFieldCodec.Snapshot(medicine);
            MedicineFieldCodec.Set(medicine, field, conflict.LosingValue);
            medicine.UpdatedAt = _clock.GetUtcNow();
            await _medicines.UpdateAsync(medicine, ct);
            await _operations.AppendAsync([.. Operations.FieldChanges(before, medicine)], ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
}

// "Dismiss": removes the entry from this device's list. The same register
// conflict comes back only if a later write makes it a conflict again.
public sealed class DismissSyncConflict
{
    private readonly ISyncConflictRepository _conflicts;
    private readonly IUnitOfWork _uow;

    public DismissSyncConflict(ISyncConflictRepository conflicts, IUnitOfWork uow)
    {
        _conflicts = conflicts;
        _uow = uow;
    }

    public Task ExecuteAsync(Guid conflictId, CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            var conflict = (await _conflicts.ListAllAsync(ct)).FirstOrDefault(c => c.Id == conflictId);
            if (conflict is null) return;
            await _conflicts.RemoveAsync(conflict, ct);
            await _uow.SaveChangesAsync(ct);
        }, cancellationToken);
}

// Turns sync off on this device (Phase 3d): the settings and the group
// key are removed; the profile data stays as it is. The other devices
// keep syncing; this device's record ages out after 90 days (§5.6).
public sealed class DisableSync
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncKeyStore _keys;
    private readonly ISyncPeerRepository _peers;
    private readonly IUnitOfWork _uow;

    public DisableSync(ISyncSettingsStore settings, ISyncKeyStore keys, ISyncPeerRepository peers, IUnitOfWork uow)
    {
        _settings = settings;
        _keys = keys;
        _peers = peers;
        _uow = uow;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(async ct =>
        {
            await _peers.ClearAsync(ct);
            await _uow.SaveChangesAsync(ct);
            _keys.Clear();
            _settings.Save(null);
        }, cancellationToken);
}
