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
//
// P8: the replicated profile settings (display name, notification
// recipients) get genesis versions too, when the profile has no version
// of them yet, so a device that joins takes the values of the device
// that wrote the genesis. Only a device writing a genesis image records
// them (create, new generation, key rotation): every other device builds
// from that image, so the genesis values agree everywhere.
public sealed class SyncGenesis
{
    public static readonly HybridTimestamp Timestamp = new(0, 0, Guid.Empty);

    private readonly IMedicineRepository _medicines;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly ISyncFieldVersionRepository _versions;
    private readonly IUnitOfWork _uow;
    private readonly IProfileSettingsStore? _profileSettings;

    public SyncGenesis(
        IMedicineRepository medicines,
        IMedicationSuspensionRepository suspensions,
        ISyncFieldVersionRepository versions,
        IUnitOfWork uow,
        IProfileSettingsStore? profileSettings = null)
    {
        _profileSettings = profileSettings;
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
        var existing = await _versions.ListAllAsync(cancellationToken);
        var written = 0;
        if (_profileSettings is not null && existing.All(v => v.EntityId != ProfileSettingsProjection.Entity))
        {
            var values = _profileSettings.Read();
            foreach (var setting in ProfileSetting.All)
            {
                await AddAsync(ProfileSettingsProjection.Entity, ProfileSettingsProjection.Entity,
                    ProfileSettingsProjection.Register(setting), values.GetValueOrDefault(setting) ?? string.Empty,
                    cancellationToken);
                written++;
            }
        }
        if (existing.Any(v => v.DeviceId == Guid.Empty && v.EntityId != ProfileSettingsProjection.Entity))
        {
            if (written > 0) await _uow.SaveChangesAsync(cancellationToken);
            return written;
        }

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
