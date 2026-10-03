using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.UseCases;

// The facts that make the plan follow a moved therapy start date
// (UpdateMedicine). Schedule rows and slot sets are facts and stay as
// they are; new ones are recorded instead, so they replicate like any
// other row:
//   - schedule: the row in force on the new start (the earliest row
//     when the start moves before every row) is recorded again,
//     effective from the new start. The days gained get a schedule,
//     and a cyclic or tapering regime counts its days from the new
//     start. When that row is not the latest recorded, the latest is
//     recorded again after it, unchanged, so the schedule summary
//     (the most recently recorded row, on every device) stays the same.
//   - slots: when the start moves earlier, the set in force on the old
//     start is recorded again, effective from the new start, just after
//     the original, so every later set keeps its precedence. A set in
//     force before a start moved later is in force from it.
// Limit: a cyclic or tapering row effective from the old start, moved
// earlier, still counts from the old start on the days after it.
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

        var rows = await schedules.ListForMedicineAsync(medicine.Id, cancellationToken);
        if (rows.Count > 0)
        {
            var source = RowInForce(rows, start) ?? RowInForce(rows, rows.Min(r => r.EffectiveFrom))!;
            if (source.EffectiveFrom != start)
            {
                var anchored = CopyOf(source, start, now);
                await schedules.AddAsync(anchored, cancellationToken);
                operations.Add(Operations.ScheduleRow(anchored));

                var latest = rows.OrderBy(r => r.RecordedAt).ThenBy(r => r.Id).Last();
                if (latest.Id != source.Id)
                {
                    var summary = CopyOf(latest, latest.EffectiveFrom, now.AddTicks(1));
                    await schedules.AddAsync(summary, cancellationToken);
                    operations.Add(Operations.ScheduleRow(summary));
                }
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
                    RecordedAt = source.Set.RecordedAt.AddTicks(1),
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

    // The latest EffectiveFrom on or before `day`, then the latest
    // recorded, as DailyConsumption.
    private static MedicationScheduleHistory? RowInForce(
        IReadOnlyList<MedicationScheduleHistory> rows, DateOnly day)
        => rows
            .Where(r => r.EffectiveFrom <= day)
            .OrderBy(r => r.EffectiveFrom)
            .ThenBy(r => r.RecordedAt)
            .ThenBy(r => r.Id)
            .LastOrDefault();

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
