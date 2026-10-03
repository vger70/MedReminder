using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Migrations;

// One-time correction of the as-needed data of a profile upgraded to
// per-slot as-needed doses (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §5.2). Before the flag existed, a slot described as "As needed" and a
// PRN medicine that kept its slots were consumed every day. For each
// medicine:
//   - current slots include one whose label is the "As needed" preset in
//     any UI language, not yet flagged: a new slot set, effective today,
//     with those slots flagged as-needed;
//   - otherwise, schedule in force today is PRN and slots exist: a new,
//     empty slot set effective today.
// Past days keep their consumption, so recorded stock counts keep their
// value; the user recovers past over-consumption with one count.
//
// The new set and its slots get ids derived from the set they replace:
// every device of a household computes the same ones, and a device that
// already has the set skips it when it arrives through sync.
//
// Runs once, while the migration is pending (IPendingDataMigrations);
// idempotent, as the marker requires. The caller holds WriteGate.
public sealed class AsNeededSlotBackfill
{
    public const string MigrationName = "AsNeededSlots";

    // Value of "Ui.AdministrationSlotDialog.Preset.AsNeeded" in every
    // assets/localization/strings.<lang>.json (pinned by a test).
    public static readonly IReadOnlyList<string> AsNeededPresetLabels =
        ["As needed", "Al bisogno", "Au besoin", "Si es necesario", "Bei Bedarf"];

    private readonly IPendingDataMigrations _migrations;
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationScheduleHistoryRepository _schedules;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public AsNeededSlotBackfill(
        IPendingDataMigrations migrations,
        IMedicineRepository medicines,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationAdministrationSlotRepository slots,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock)
    {
        _migrations = migrations;
        _medicines = medicines;
        _schedules = schedules;
        _slots = slots;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

    public static bool IsAsNeededLabel(string? label)
        => label is not null
           && AsNeededPresetLabels.Any(p => string.Equals(p, label.Trim(), StringComparison.OrdinalIgnoreCase));

    // Returns the number of medicines whose slots were changed.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _migrations.IsPendingAsync(MigrationName, cancellationToken)) return 0;

        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _clock.LocalTimeZone).DateTime);

        var changed = 0;
        var operations = new List<SyncOperationBody>();
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            var sets = await _slots.ListSetsForMedicineAsync(medicine.Id, cancellationToken);
            var current = sets.Count == 0 ? null : sets[^1];
            if (current is null || current.Slots.Count == 0) continue;

            IReadOnlyList<MedicationAdministrationSlot>? replacement = null;
            if (current.Slots.Any(s => !s.IsAsNeeded && IsAsNeededLabel(s.TimingLabel)))
            {
                replacement = current.Slots
                    .Select(s => (Slot: s, AsNeeded: s.IsAsNeeded || IsAsNeededLabel(s.TimingLabel)))
                    .Select(x => new MedicationAdministrationSlot
                    {
                        MedicineId = medicine.Id,
                        Dose = x.Slot.Dose,
                        Time = x.Slot.Time,
                        TimingLabel = x.Slot.TimingLabel,
                        Order = x.Slot.Order,
                        IsAsNeeded = x.AsNeeded,
                    })
                    .ToList();
            }
            else if (IsPrnOn(today, await _schedules.ListForMedicineAsync(medicine.Id, cancellationToken)))
            {
                replacement = [];
            }
            if (replacement is null) continue;

            var setId = DeterministicGuid.Create(medicine.Id, $"as-needed-backfill:{current.Set.Id:N}");
            if (sets.Any(e => e.Set.Id == setId)) continue;

            var set = new MedicationAdministrationSlotSet
            {
                Id = setId,
                MedicineId = medicine.Id,
                EffectiveFrom = today < medicine.StartDate ? medicine.StartDate : today,
                // The current set is the latest recorded: never tie with
                // or precede the set it replaces (clock skew).
                RecordedAt = now > current.Set.RecordedAt ? now : current.Set.RecordedAt.AddTicks(1),
            };
            var slots = replacement
                .Select(s => new MedicationAdministrationSlot
                {
                    Id = DeterministicGuid.Create(medicine.Id, $"as-needed-backfill:{current.Set.Id:N}:{s.Order}"),
                    MedicineId = s.MedicineId,
                    SetId = setId,
                    Dose = s.Dose,
                    Time = s.Time,
                    TimingLabel = s.TimingLabel,
                    Order = s.Order,
                    IsAsNeeded = s.IsAsNeeded,
                })
                .ToList();
            await _slots.AddSetAsync(set, slots, cancellationToken);
            operations.Add(Operations.SlotSet(set, slots));
            changed++;
        }

        if (operations.Count > 0)
        {
            await _operations.AppendAsync(operations, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        await _migrations.CompleteAsync(MigrationName, cancellationToken);
        return changed;
    }

    // Schedule row in force on `day`: the latest EffectiveFrom, then the
    // latest recorded (rows come in recording order), as DailyConsumption.
    private static bool IsPrnOn(DateOnly day, IReadOnlyList<MedicationScheduleHistory> rows)
    {
        MedicationScheduleHistory? inForce = null;
        foreach (var row in rows)
        {
            if (row.EffectiveFrom > day) continue;
            if (inForce is null || row.EffectiveFrom >= inForce.EffectiveFrom) inForce = row;
        }
        return inForce?.ScheduleKind == ScheduleKind.Prn;
    }
}
