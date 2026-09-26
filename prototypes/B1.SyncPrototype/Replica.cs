using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Domain.Medicines;

namespace MedReminder.Prototypes.Sync;

public sealed class ReplicatedSuspension
{
    public Hlc? CreatedHlc { get; set; }
    public DateOnly StartDate { get; set; }
    public Register<DateOnly?> EndDate { get; } = new();
}

public sealed class ReplicatedIntake
{
    public IntakeRecorded? Recorded { get; set; }
    public Register<IntakeStatus> Status { get; } = new();
}

// Replicated state of one medicine. Every container is a set or a
// versioned register, so applying the same operations in any order
// yields the same state.
public sealed class MedicineState
{
    public MedicineState(Guid id) => Id = id;

    public Guid Id { get; }
    public MedicineCreated? Created { get; set; }
    public Dictionary<MedicineField, Register<string?>> Fields { get; } = new();
    public Dictionary<Guid, ActivitySet> Activity { get; } = new();
    public Dictionary<DateOnly, Register<IReadOnlyList<SlotValue>>> Slots { get; } = new();
    public Dictionary<DateOnly, Register<(decimal Dose, int Administrations)>> Schedule { get; } = new();
    public Dictionary<Guid, ReplicatedSuspension> Suspensions { get; } = new();
    public Dictionary<Guid, StockEntryAdded> Entries { get; } = new();
    public Dictionary<Guid, StockCountRecorded> Counts { get; } = new();
    public Dictionary<Guid, ReplicatedIntake> Intakes { get; } = new();
    public Dictionary<Guid, LegacyMovement> LegacyMovements { get; } = new();
    public Dictionary<Guid, LegacyIntake> LegacyIntakes { get; } = new();
    public LegacyBaseline? Baseline { get; set; }

    public Register<string?> Field(MedicineField field)
    {
        if (!Fields.TryGetValue(field, out var register))
        {
            register = new Register<string?>();
            Fields[field] = register;
        }
        return register;
    }
}

public sealed class Replica
{
    private readonly List<Operation> _log = new();
    private readonly HashSet<Guid> _applied = new();

    public Dictionary<Guid, MedicineState> Medicines { get; } = new();

    // Retracted fact id -> earliest retraction HLC.
    public Dictionary<Guid, Hlc> Tombstones { get; } = new();

    // Same schedule key written by two different devices (§4.5). Local,
    // not part of the replicated state.
    public List<(Guid MedicineId, DateOnly EffectiveFrom)> ScheduleConflicts { get; } = new();

    public IReadOnlyList<Operation> Log => _log;

    public int ApplyCount { get; private set; }

    public bool Contains(Guid opId) => _applied.Contains(opId);

    // Idempotent: an operation already applied is ignored (R3).
    public bool Apply(Operation op)
    {
        if (!_applied.Add(op.OpId)) return false;
        _log.Add(op);
        ApplyCount++;

        if (op is FactRetracted r)
        {
            if (!Tombstones.TryGetValue(r.FactId, out var existing) || r.Hlc < existing)
                Tombstones[r.FactId] = r.Hlc;
            return true;
        }

        var m = Get(op.MedicineId);
        switch (op)
        {
            case MedicineCreated c:
                m.Created = c;
                break;
            case MedicineFieldSet f:
                m.Field(f.Field).Set(f.Hlc, f.Value);
                break;
            case ActivitySet a:
                m.Activity[a.OpId] = a;
                break;
            case SlotsSet s:
                if (!m.Slots.TryGetValue(s.EffectiveFrom, out var slots))
                    m.Slots[s.EffectiveFrom] = slots = new Register<IReadOnlyList<SlotValue>>();
                slots.Set(s.Hlc, s.Slots);
                break;
            case ScheduleRowAdded row:
                if (!m.Schedule.TryGetValue(row.EffectiveFrom, out var sched))
                    m.Schedule[row.EffectiveFrom] = sched = new Register<(decimal, int)>();
                if (sched.HasValue && sched.Versions.Any(v => v.Key.DeviceId != row.Hlc.DeviceId))
                    ScheduleConflicts.Add((row.MedicineId, row.EffectiveFrom));
                sched.Set(row.Hlc, (row.Dose, row.AdministrationsPerDay));
                break;
            case SuspensionCreated sc:
                var susp = Suspension(m, sc.SuspensionId);
                susp.CreatedHlc = sc.Hlc;
                susp.StartDate = sc.StartDate;
                break;
            case SuspensionEndSet se:
                Suspension(m, se.SuspensionId).EndDate.Set(se.Hlc, se.EndDate);
                break;
            case StockEntryAdded e:
                m.Entries[e.FactId] = e;
                break;
            case StockCountRecorded k:
                m.Counts[k.FactId] = k;
                break;
            case IntakeRecorded i:
                var intake = Intake(m, i.IntakeId);
                intake.Recorded = i;
                intake.Status.Set(i.Hlc, i.Status);
                break;
            case IntakeStatusSet ist:
                Intake(m, ist.IntakeId).Status.Set(ist.Hlc, ist.Status);
                break;
            case LegacyMovement lm:
                m.LegacyMovements[lm.FactId] = lm;
                break;
            case LegacyIntake li:
                m.LegacyIntakes[li.IntakeId] = li;
                break;
            case LegacyBaseline b:
                m.Baseline = b;
                break;
            default:
                throw new InvalidOperationException($"Unknown operation {op.GetType().Name}.");
        }
        return true;
    }

    public MedicineState Get(Guid medicineId)
    {
        if (!Medicines.TryGetValue(medicineId, out var m))
        {
            m = new MedicineState(medicineId);
            Medicines[medicineId] = m;
        }
        return m;
    }

    public bool IsRetracted(Guid factId, Hlc? asOf)
        => Tombstones.TryGetValue(factId, out var hlc) && (asOf is null || hlc < asOf.Value);

    // Rebuilds a replica from a set of operations applied in HLC order.
    // Used as the reference that incremental, arbitrary-order
    // application must match.
    public static Replica FoldSorted(IEnumerable<Operation> ops)
    {
        var r = new Replica();
        foreach (var op in ops.OrderBy(o => o.Hlc).ThenBy(o => o.OpId))
            r.Apply(op);
        return r;
    }

    // Checkpoint (§5.6): the replicated state. Registers keep their full
    // history, so the state is equivalent to the operation set; a clone
    // by re-application is therefore an exact checkpoint.
    public Replica CloneAsCheckpoint()
    {
        var r = new Replica();
        foreach (var op in _log) r.Apply(op);
        return r;
    }

    // Hash of the replicated state only (derived rows excluded, §12).
    public string StateHash()
    {
        var sb = new StringBuilder();
        foreach (var (id, hlc) in Tombstones.OrderBy(t => t.Key))
            sb.Append("T:").Append(id).Append('@').Append(hlc).Append('\n');

        foreach (var m in Medicines.Values.OrderBy(m => m.Id))
        {
            sb.Append("M:").Append(m.Id).Append('\n');
            if (m.Created is { } c)
                sb.Append(" C:").Append(c.StartDate.ToString("O", CultureInfo.InvariantCulture)).Append(c.Unit).Append('@').Append(c.Hlc).Append('\n');
            foreach (var (field, reg) in m.Fields.OrderBy(f => f.Key))
                foreach (var (hlc, v) in reg.Versions)
                    sb.Append(" F:").Append(field).Append('=').Append(v).Append('@').Append(hlc).Append('\n');
            foreach (var a in m.Activity.Values.OrderBy(a => a.OpId))
                sb.Append(" A:").Append(a.Active).Append(a.Day.ToString("O", CultureInfo.InvariantCulture)).Append('@').Append(a.Hlc).Append('\n');
            foreach (var (day, reg) in m.Slots.OrderBy(s => s.Key))
                foreach (var (hlc, v) in reg.Versions)
                    sb.Append(" S:").Append(day.ToString("O", CultureInfo.InvariantCulture)).Append('=')
                      .Append(string.Join(',', v.Select(s => $"{s.Dose.ToString(CultureInfo.InvariantCulture)}/{s.Time}")))
                      .Append('@').Append(hlc).Append('\n');
            foreach (var (day, reg) in m.Schedule.OrderBy(s => s.Key))
                foreach (var (hlc, v) in reg.Versions)
                    sb.Append(" H:").Append(day.ToString("O", CultureInfo.InvariantCulture)).Append('=')
                      .Append(v.Dose.ToString(CultureInfo.InvariantCulture)).Append('x').Append(v.Administrations)
                      .Append('@').Append(hlc).Append('\n');
            foreach (var (id, s) in m.Suspensions.OrderBy(s => s.Key))
            {
                sb.Append(" P:").Append(id).Append(s.StartDate.ToString("O", CultureInfo.InvariantCulture)).Append('@').Append(s.CreatedHlc).Append('\n');
                foreach (var (hlc, v) in s.EndDate.Versions)
                    sb.Append("  E:").Append(v?.ToString("O", CultureInfo.InvariantCulture)).Append('@').Append(hlc).Append('\n');
            }
            foreach (var e in m.Entries.Values.OrderBy(e => e.FactId))
                sb.Append(" N:").Append(e.FactId).Append(e.Kind).Append(e.Delta.ToString(CultureInfo.InvariantCulture)).Append('@').Append(e.Hlc).Append('\n');
            foreach (var k in m.Counts.Values.OrderBy(k => k.FactId))
                sb.Append(" K:").Append(k.FactId).Append(k.Counted.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(k.TakenToday.ToString(CultureInfo.InvariantCulture)).Append('@').Append(k.Hlc).Append('\n');
            foreach (var (id, i) in m.Intakes.OrderBy(i => i.Key))
            {
                sb.Append(" I:").Append(id).Append(i.Recorded?.Day.ToString("O", CultureInfo.InvariantCulture))
                  .Append(i.Recorded?.Quantity.ToString(CultureInfo.InvariantCulture)).Append('@').Append(i.Recorded?.Hlc).Append('\n');
                foreach (var (hlc, v) in i.Status.Versions)
                    sb.Append("  s:").Append(v).Append('@').Append(hlc).Append('\n');
            }
            foreach (var l in m.LegacyMovements.Values.OrderBy(l => l.FactId))
                sb.Append(" L:").Append(l.FactId).Append(l.Kind).Append(l.Delta.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var l in m.LegacyIntakes.Values.OrderBy(l => l.IntakeId))
                sb.Append(" J:").Append(l.IntakeId).Append(l.Status).Append('\n');
            if (m.Baseline is { } b)
                sb.Append(" B:").Append(b.CutoffDay.ToString("O", CultureInfo.InvariantCulture)).Append('/').Append(b.Epoch).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static ReplicatedSuspension Suspension(MedicineState m, Guid id)
    {
        if (!m.Suspensions.TryGetValue(id, out var s))
        {
            s = new ReplicatedSuspension();
            m.Suspensions[id] = s;
        }
        return s;
    }

    private static ReplicatedIntake Intake(MedicineState m, Guid id)
    {
        if (!m.Intakes.TryGetValue(id, out var i))
        {
            i = new ReplicatedIntake();
            m.Intakes[id] = i;
        }
        return i;
    }
}
