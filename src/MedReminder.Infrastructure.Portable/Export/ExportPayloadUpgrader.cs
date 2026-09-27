using MedReminder.Application.Export;

namespace MedReminder.Infrastructure.Export;

// Brings a decrypted payload of an older schema version to the current
// entity model before ProfileDatabaseBuilder writes it
// (docs/EXPORT-FORMAT.md §5). Mutates the payload in place; pure apart
// from the clock passed in.
//
// Schema version 1 -> 2 (B.1 Phase 2b, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §3.5, §7.3): the archive predates the
// facts / derived split, so it is treated like a database the boot
// patch meets for the first time:
//   - stock movements without an origin become Legacy (ExportMapper);
//   - the slots of each medicine become one slot set, in force from
//     the medicine's StartDate; the set reuses the medicine's Id, as
//     the boot patch does;
//   - the ledger cutoff is the day before the import, frozen at the
//     import instant.
internal static class ExportPayloadUpgrader
{
    public static void UpgradeToCurrent(ExportPayload payload, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(clock);

        if (payload.SchemaVersion >= 2)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).DateTime);

        var startDates = payload.Medicines.ToDictionary(m => m.Id, m => m.StartDate);
        foreach (var medicineId in payload.MedicationAdministrationSlots
                     .Select(s => s.MedicineId)
                     .Distinct())
        {
            payload.MedicationAdministrationSlotSets.Add(new ExportedAdministrationSlotSet
            {
                Id = medicineId,
                MedicineId = medicineId,
                EffectiveFrom = startDates.TryGetValue(medicineId, out var start)
                    ? start
                    : throw new InvalidOperationException(
                        $"Slot references unknown medicine {medicineId}."),
                RecordedAt = now,
            });
        }
        foreach (var slot in payload.MedicationAdministrationSlots)
        {
            slot.SetId = slot.MedicineId;
        }

        payload.LedgerCutoff = new ExportedLedgerCutoff
        {
            CutoffDay = localToday.AddDays(-1),
            FrozenAt = now,
        };

        payload.SchemaVersion = ExportFormat.CurrentSchemaVersion;
    }
}
