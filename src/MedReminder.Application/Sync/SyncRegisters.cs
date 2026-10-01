using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// The last-writer-wins registers of the replicated state and their
// conflicts (B.1 Phase 3b, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md
// §4.2, §4.5). Shared by the local operation log (a local write adds a
// version) and by the apply step (a remote write adds a version and
// wins or loses by HLC).
//
// Scoped, with an in-memory copy of the versions and conflicts it has
// read or added, because a unit of work can add several versions of one
// register before it saves. Callers hold WriteGate.
public sealed class SyncRegisters
{
    public const string IsActive = "IsActive";
    public const string EndDate = "EndDate";
    public const string SlotSet = "SlotSet";
    public const string LatestSchedule = "LatestSchedule";
    // The whole state of a prescription, as the payload of the
    // PrescriptionChanged that wrote it (last writer wins).
    public const string PrescriptionState = "Prescription";

    private readonly ISyncFieldVersionRepository _versions;
    private readonly ISyncConflictRepository _conflicts;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Guid, List<SyncFieldVersion>> _versionsByEntity = new();
    private readonly Dictionary<Guid, List<SyncConflict>> _conflictsBySubject = new();

    public SyncRegisters(
        ISyncFieldVersionRepository versions,
        ISyncConflictRepository conflicts,
        TimeProvider clock)
    {
        _versions = versions;
        _conflicts = conflicts;
        _clock = clock;
    }

    // Register of the schedule row with a given EffectiveFrom (§4.2:
    // keyed fact, higher HLC wins).
    public static string ScheduleOn(DateOnly effectiveFrom)
        => "Schedule@" + effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The register writes an operation carries: which registers, with
    // which values, and which one can raise a conflict (the one its
    // BaseVersion refers to). Operations not listed write no register.
    public static IReadOnlyList<RegisterWrite> WritesOf(SyncOperationBody body) => body switch
    {
        MedicineCreated c =>
        [
            .. c.Fields.Select(f => new RegisterWrite(c.MedicineId, f.Field, f.Value, null)),
            new RegisterWrite(c.MedicineId, IsActive, "true", null),
        ],
        MedicineFieldChanged f =>
            [new RegisterWrite(f.MedicineId, f.Field, f.Value, SyncConflictKind.MedicineField)],
        MedicineActivityChanged a =>
            [new RegisterWrite(a.MedicineId, IsActive, a.Active ? "true" : "false", null)],
        ScheduleRowRecorded s =>
        [
            new RegisterWrite(s.MedicineId, ScheduleOn(s.EffectiveFrom), Id(s.RowId), SyncConflictKind.ScheduleSameDate),
            new RegisterWrite(s.MedicineId, LatestSchedule, Id(s.RowId), null),
        ],
        SlotSetRecorded s =>
            [new RegisterWrite(s.MedicineId, SlotSet, Id(s.SetId), SyncConflictKind.SlotSetReplaced)],
        SuspensionRecorded s =>
            [new RegisterWrite(s.SuspensionId, EndDate, Date(s.EndDate), null)],
        SuspensionEndChanged s =>
            [new RegisterWrite(s.SuspensionId, EndDate, Date(s.EndDate), null)],
        ProfileSettingChanged p =>
            [new RegisterWrite(ProfileSettingsProjection.Entity, ProfileSettingsProjection.Register(p.Setting), p.Value, null)],
        PrescriptionChanged p =>
            [new RegisterWrite(p.PrescriptionId, PrescriptionState, OperationCodec.Serialize(p).Payload, null)],
        _ => [],
    };

    // The base version to stamp on a local operation: the current winner
    // of the register its BaseVersion refers to.
    public async Task<SyncOperationBody> WithBaseAsync(SyncOperationBody body, CancellationToken cancellationToken)
    {
        var conflicting = WritesOf(body).FirstOrDefault(w => w.ConflictKind is not null);
        if (conflicting is null) return body;
        var current = (await WinnerAsync(conflicting.EntityId, conflicting.Register, cancellationToken))?.Version;
        return body switch
        {
            MedicineFieldChanged f => f with { BaseVersion = current },
            ScheduleRowRecorded s => s with { BaseVersion = current },
            SlotSetRecorded s => s with { BaseVersion = current },
            _ => body,
        };
    }

    // The prescription a PrescriptionState version holds.
    public static PrescriptionChanged ParsePrescription(string value)
        => (PrescriptionChanged)OperationCodec.Deserialize(
            nameof(PrescriptionChanged), OperationCodec.CurrentSchemaVersion, value);

    public static HybridTimestamp? BaseOf(SyncOperationBody body) => body switch
    {
        MedicineFieldChanged f => f.BaseVersion,
        ScheduleRowRecorded s => s.BaseVersion,
        SlotSetRecorded s => s.BaseVersion,
        _ => null,
    };

    public async Task<SyncFieldVersion?> WinnerAsync(Guid entityId, string register, CancellationToken cancellationToken)
        => (await ListAsync(entityId, register, cancellationToken)).MaxBy(v => v.Version);

    public async Task<IReadOnlyList<SyncFieldVersion>> ListAsync(
        Guid entityId, string register, CancellationToken cancellationToken)
        => [.. (await LoadVersionsAsync(entityId, cancellationToken)).Where(v => v.Register == register)];

    // Records every register write of an operation stamped `timestamp`.
    // Returns, per register, whether the new version is now the winner.
    public async Task<IReadOnlyDictionary<string, bool>> RecordAsync(
        Guid medicineId,
        SyncOperationBody body,
        HybridTimestamp timestamp,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var @base = BaseOf(body);
        foreach (var write in WritesOf(body))
        {
            var entityBase = write.ConflictKind is null ? null : @base;
            var version = new SyncFieldVersion
            {
                MedicineId = medicineId,
                EntityId = write.EntityId,
                Register = write.Register,
                HlcPhysicalMs = timestamp.PhysicalMs,
                HlcCounter = timestamp.Counter,
                DeviceId = timestamp.DeviceId,
                Value = write.Value,
                BasePhysicalMs = entityBase?.PhysicalMs,
                BaseCounter = entityBase?.Counter,
                BaseDeviceId = entityBase?.DeviceId,
            };
            var list = await LoadVersionsAsync(write.EntityId, cancellationToken);
            list.Add(version);
            await _versions.AddAsync(version, cancellationToken);

            var winner = list.Where(v => v.Register == write.Register).MaxBy(v => v.Version)!;
            result[write.Register] = winner.Id == version.Id;

            if (write.ConflictKind is { } kind)
            {
                await RefreshConflictsAsync(medicineId, write.EntityId, write.Register, kind, cancellationToken);
            }
        }
        return result;
    }

    // Adds a hint conflict unless the same one is already listed.
    public async Task AddHintAsync(SyncConflict conflict, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        var list = await LoadConflictsAsync(conflict.SubjectId, cancellationToken);
        if (list.Any(c => c.Id == conflict.Id)) return;
        list.Add(conflict);
        await _conflicts.AddAsync(conflict, cancellationToken);
    }

    public static Guid ConflictId(Guid medicineId, SyncConflictKind kind, Guid subjectId, string? register, string loser)
        => DeterministicGuid.Create(medicineId, $"conflict:{(int)kind}:{subjectId:N}:{register}:{loser}");

    // Register conflicts are a function of the versions: the listed ones
    // become exactly the versions concurrent with the winner.
    private async Task RefreshConflictsAsync(
        Guid medicineId, Guid entityId, string register, SyncConflictKind kind, CancellationToken cancellationToken)
    {
        var versions = await ListAsync(entityId, register, cancellationToken);
        var winner = versions.MaxBy(v => v.Version)!;
        var merge = versions.ToDictionary(v => v.Version, v => new RegisterMerge.Version(v.Version, v.Base, v.Value));
        var losers = RegisterMerge.Losers(merge.Values);

        var desired = losers.Select(l => new SyncConflict
        {
            Id = ConflictId(medicineId, kind, entityId, register, $"{l.Timestamp}>{winner.Version}"),
            Kind = kind,
            MedicineId = medicineId,
            SubjectId = entityId,
            Register = register,
            WinningValue = winner.Value,
            LosingValue = l.Value,
            WinningDeviceId = winner.DeviceId,
            LosingDeviceId = l.Timestamp.DeviceId,
            DetectedAt = _clock.GetUtcNow(),
        }).ToList();

        var listed = await LoadConflictsAsync(entityId, cancellationToken);
        foreach (var stale in listed.Where(c => c.Register == register && c.Kind == kind)
                     .Where(c => desired.All(d => d.Id != c.Id)).ToList())
        {
            listed.Remove(stale);
            await _conflicts.RemoveAsync(stale, cancellationToken);
        }
        // The id names the loser and the winner, so a new winner means a
        // new row and an unchanged pair is never written twice.
        foreach (var conflict in desired.Where(d => listed.All(c => c.Id != d.Id)))
        {
            listed.Add(conflict);
            await _conflicts.AddAsync(conflict, cancellationToken);
        }
    }

    private async Task<List<SyncFieldVersion>> LoadVersionsAsync(Guid entityId, CancellationToken cancellationToken)
    {
        if (!_versionsByEntity.TryGetValue(entityId, out var list))
        {
            list = [.. await _versions.ListForEntityAsync(entityId, cancellationToken)];
            _versionsByEntity[entityId] = list;
        }
        return list;
    }

    private async Task<List<SyncConflict>> LoadConflictsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        if (!_conflictsBySubject.TryGetValue(subjectId, out var list))
        {
            list = [.. await _conflicts.ListForSubjectAsync(subjectId, cancellationToken)];
            _conflictsBySubject[subjectId] = list;
        }
        return list;
    }

    private static string Id(Guid id) => id.ToString("D");

    private static string? Date(DateOnly? day) => day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed record RegisterWrite(Guid EntityId, string Register, string? Value, SyncConflictKind? ConflictKind);
