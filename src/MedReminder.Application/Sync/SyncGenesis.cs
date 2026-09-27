using MedReminder.Application.Abstractions;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// Genesis versions of the registers (B.1 Phase 3b-2, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.5): when sync is enabled, every register
// of the existing medicines and suspensions gets a version holding its
// current value at the genesis timestamp, (0, 0, empty device), older
// than any write. The genesis snapshot carries them to the other
// devices, so every device knows the value a register had before its
// first synced write: the count re-evaluation reads end dates "as of" an
// instant, and a register write always has a base.
//
// Idempotent: nothing is written when genesis versions already exist.
// Called by the enable-sync flow (Phase 3d).
public sealed class SyncGenesis
{
    public static readonly HybridTimestamp Timestamp = new(0, 0, Guid.Empty);

    private readonly IMedicineRepository _medicines;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly ISyncFieldVersionRepository _versions;
    private readonly IUnitOfWork _uow;

    public SyncGenesis(
        IMedicineRepository medicines,
        IMedicationSuspensionRepository suspensions,
        ISyncFieldVersionRepository versions,
        IUnitOfWork uow)
    {
        _medicines = medicines;
        _suspensions = suspensions;
        _versions = versions;
        _uow = uow;
    }

    // Returns the number of versions written.
    public Task<int> RecordAsync(CancellationToken cancellationToken)
        => WriteGate.RunExclusiveAsync(RecordCoreAsync, cancellationToken);

    private async Task<int> RecordCoreAsync(CancellationToken cancellationToken)
    {
        if ((await _versions.ListAllAsync(cancellationToken)).Any(v => v.DeviceId == Guid.Empty)) return 0;

        var written = 0;
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            foreach (var field in MedicineFieldCodec.Snapshot(medicine))
            {
                await AddAsync(medicine.Id, medicine.Id, field.Field, field.Value, cancellationToken);
                written++;
            }
            await AddAsync(medicine.Id, medicine.Id, SyncRegisters.IsActive, medicine.IsActive ? "true" : "false", cancellationToken);
            written++;

            foreach (var suspension in await _suspensions.ListForMedicineAsync(medicine.Id, cancellationToken))
            {
                await AddAsync(medicine.Id, suspension.Id, SyncRegisters.EndDate,
                    suspension.EndDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    cancellationToken);
                written++;
            }
        }

        if (written > 0) await _uow.SaveChangesAsync(cancellationToken);
        return written;
    }

    private Task AddAsync(Guid medicineId, Guid entityId, string register, string? value, CancellationToken ct)
        => _versions.AddAsync(new SyncFieldVersion
        {
            MedicineId = medicineId,
            EntityId = entityId,
            Register = register,
            HlcPhysicalMs = Timestamp.PhysicalMs,
            HlcCounter = Timestamp.Counter,
            DeviceId = Timestamp.DeviceId,
            Value = value,
        }, ct);
}
