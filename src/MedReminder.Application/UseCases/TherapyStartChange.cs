using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.UseCases;

// The facts that make the plan follow a moved therapy start date
// (UpdateMedicine). Schedule rows and slot sets are facts and stay as
// they are; new ones are recorded instead, so they replicate like any
// other row:
//   - schedule: the row effective from the old start (the initial
//     plan) is recorded again, effective from the new start, when the
//     start moves earlier (the days gained get a schedule) or when it
//     moves later and the initial plan is still the one in force there
//     and counts days from its start (cyclic, tapering). A schedule
//     change recorded after the old start is never re-anchored. When
//     the copied row is not the latest recorded, the latest is recorded
//     again after it, unchanged, so the schedule summary (the most
//     recently recorded row, on every device) stays the same.
//   - slots: when the start moves earlier, the set in force on the old
//     start is recorded again, effective from the new start, just
//     before the original: it covers only the days gained and never
//     competes with a set recorded after the original. A set in force
//     before a start moved later is in force from it.
// Limits: a cyclic or tapering row moved earlier still counts from the
// old start on the days after it; two devices moving the start at the
// same time each record their copy, and the losing device's copy stays
// among the facts.
internal static class TherapyStartChange
{
    public static async Task<IReadOnlyList<SyncOperationBody>> RecordAsync(
        Medicine medicine,
        DateOnly previousStart,
        DateTimeOffset now,
        IMedicationScheduleHistoryRepository schedules,
        IMedicationAdministrationSlotRepository slots,
        CancellationToken cancellationToken)
    {
        var start = medicine.StartDate;
        var operations = new List<SyncOperationBody> { new MedicineStartChanged(medicine.Id, start) };

        var rows = (await schedules.ListForMedicineAsync(medicine.Id, cancellationToken))
            .OrderBy(r => r.EffectiveFrom)
            .ThenBy(r => r.RecordedAt)
            .ThenBy(r => r.Id)
            .ToList();
        if (DailyConsumption.RowInForce(previousStart, rows) is { } initial
            && initial.EffectiveFrom == previousStart
            && (start < previousStart
                || (CountsFromStart(initial.ScheduleKind) && DailyConsumption.RowInForce(start, rows) == initial)))
        {
            var anchored = CopyOf(initial, start, now);
            await schedules.AddAsync(anchored, cancellationToken);
            operations.Add(Operations.ScheduleRow(anchored));

            var latest = rows.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).Last();
            if (latest.Id != initial.Id)
            {
                var summary = CopyOf(latest, latest.EffectiveFrom, now.AddTicks(1));
                await schedules.AddAsync(summary, cancellationToken);
                operations.Add(Operations.ScheduleRow(summary));
            }
        }

        if (start < previousStart)
        {
            var sets = await slots.ListSetsForMedicineAsync(medicine.Id, cancellationToken);
            var source = sets
                .Where(e => e.Set.EffectiveFrom <= previousStart)
                .OrderBy(e => e.Set.RecordedAt)
                .ThenBy(e => e.Set.Id)
                .LastOrDefault();
            if (source is { Slots.Count: > 0 } && source.Set.EffectiveFrom > start)
            {
                var set = new MedicationAdministrationSlotSet
                {
                    MedicineId = medicine.Id,
                    EffectiveFrom = start,
                    RecordedAt = source.Set.RecordedAt.AddTicks(-1),
                };
                var copied = source.Slots
                    .Select(s => new MedicationAdministrationSlot
                    {
                        MedicineId = medicine.Id,
                        SetId = set.Id,
                        Dose = s.Dose,
                        Time = s.Time,
                        TimingLabel = s.TimingLabel,
                        Order = s.Order,
                        IsAsNeeded = s.IsAsNeeded,
                        PresetId = s.PresetId,
                    })
                    .ToList();
                await slots.AddSetAsync(set, copied, cancellationToken);
                operations.Add(Operations.SlotSet(set, copied));
            }
        }

        return operations;
    }

    private static bool CountsFromStart(ScheduleKind kind)
        => kind is ScheduleKind.Cyclic or ScheduleKind.Tapering or ScheduleKind.SteppedTapering;

    private static MedicationScheduleHistory CopyOf(
        MedicationScheduleHistory row, DateOnly effectiveFrom, DateTimeOffset recordedAt)
        => new()
        {
            MedicineId = row.MedicineId,
            EffectiveFrom = effectiveFrom,
            DosePerAdministration = row.DosePerAdministration,
            AdministrationsPerDay = row.AdministrationsPerDay,
            ScheduleKind = row.ScheduleKind,
            SchedulePayload = row.SchedulePayload,
            RecordedAt = recordedAt,
        };
}
