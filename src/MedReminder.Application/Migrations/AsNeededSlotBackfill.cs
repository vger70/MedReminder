using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Migrations;

// One-time correction of the as-needed data of a profile upgraded to
// per-slot as-needed doses (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §5.2). Before the flag existed, a slot described as "As needed" was
// consumed every day. For each medicine:
//   - current slots include one whose label is the "As needed" preset in
//     any UI language, not yet flagged: a new slot set, effective today,
//     with those slots flagged as-needed.
// The slots of a PRN medicine stay: under a non-FixedDaily schedule they
// only place the schedule's quantity, which PRN does not have.
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

    private readonly IPendingDataMigrations _migrations;
    private readonly IMedicineRepository _medicines;
    private readonly IMedicationAdministrationSlotRepository _slots;
    private readonly IOperationLog _operations;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;
    private readonly BuiltInPresetLabels _labels;

    public AsNeededSlotBackfill(
        IPendingDataMigrations migrations,
        IMedicineRepository medicines,
        IMedicationAdministrationSlotRepository slots,
        IOperationLog operations,
        IUnitOfWork uow,
        TimeProvider clock,
        ILocalizationService localization)
    {
        _labels = new BuiltInPresetLabels(localization);
        _migrations = migrations;
        _medicines = medicines;
        _slots = slots;
        _operations = operations;
        _uow = uow;
        _clock = clock;
    }

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
            if (current.Slots.Any(s => !s.IsAsNeeded && _labels.IsAsNeeded(s.TimingLabel)))
            {
                replacement = current.Slots
                    .Select(s => (Slot: s, AsNeeded: s.IsAsNeeded || _labels.IsAsNeeded(s.TimingLabel)))
                    .Select(x => new MedicationAdministrationSlot
                    {
                        MedicineId = medicine.Id,
                        Dose = x.Slot.Dose,
                        Time = x.Slot.Time,
                        TimingLabel = x.Slot.TimingLabel,
                        Order = x.Slot.Order,
                        IsAsNeeded = x.AsNeeded,
                        PresetId = x.Slot.PresetId,
                    })
                    .ToList();
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
                    PresetId = s.PresetId,
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
}
