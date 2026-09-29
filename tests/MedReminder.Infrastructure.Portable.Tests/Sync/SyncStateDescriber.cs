using System.Globalization;
using System.Text;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.Infrastructure.Tests.Sync;

internal static class SyncStateDescriber
{
    // Canonical text of the replicated state, the register conflicts and
    // the derived ledger. Device-local data is left out: operation and
    // version rows, hint conflicts, notification events, UpdatedAt, and
    // the stored epoch of user stock entries.
    public static Task<string> DescribeAsync(SyncDevice device)
        => device.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            var sb = new StringBuilder();
            foreach (var m in await db.Medicines.AsNoTracking().OrderBy(m => m.Id).ToListAsync())
            {
                sb.AppendLine($"M {m.Id} start={m.StartDate:O} active={m.IsActive} dose={m.DosePerAdministration} " +
                    $"x{m.AdministrationsPerDay} epoch={m.StockEpoch}/{m.StockEpochFactId} " +
                    string.Join("|", MedicineFieldCodec.Snapshot(m).Select(v => $"{v.Field}={v.Value}")));
            }
            foreach (var r in (await db.StockMovements.AsNoTracking().ToListAsync()).OrderBy(r => r.Id))
            {
                var epoch = r.Origin == StockMovementOrigin.Derived ? r.StockEpoch.ToString(CultureInfo.InvariantCulture) : "-";
                sb.AppendLine($"S {r.Id} {r.MedicineId} {r.Kind} {r.QuantityDelta} {r.OccurredAt:O} {r.Origin} e={epoch} {r.Notes}");
            }
            foreach (var i in (await db.MedicationIntakes.AsNoTracking().ToListAsync()).OrderBy(i => i.Id))
                sb.AppendLine($"I {i.Id} {i.Day:O} {i.Status} {i.Quantity} {i.RecordedAt:O}");
            foreach (var c in (await db.StockCounts.AsNoTracking().ToListAsync()).OrderBy(c => c.Id))
                sb.AppendLine($"C {c.Id} {c.CountDay:O} {c.CountedQuantity} {c.Correction} {c.AdvancesEpoch}");
            foreach (var s in (await db.MedicationSuspensions.AsNoTracking().ToListAsync()).OrderBy(s => s.Id))
                sb.AppendLine($"P {s.Id} {s.StartDate:O} {s.EndDate:O} {s.Reason}");
            foreach (var h in (await db.MedicationScheduleHistories.AsNoTracking().ToListAsync()).OrderBy(h => h.Id))
                sb.AppendLine($"H {h.Id} {h.EffectiveFrom:O} {h.DosePerAdministration} {h.AdministrationsPerDay} {h.ScheduleKind}");
            foreach (var s in (await db.MedicationAdministrationSlotSets.AsNoTracking().ToListAsync()).OrderBy(s => s.Id))
                sb.AppendLine($"T {s.Id} {s.EffectiveFrom:O}");
            foreach (var s in (await db.MedicationAdministrationSlots.AsNoTracking().ToListAsync()).OrderBy(s => s.Id))
                sb.AppendLine($"L {s.Id} {s.SetId} {s.Dose} {s.Time}");
            foreach (var a in (await db.MedicineActivityChanges.AsNoTracking().ToListAsync()).OrderBy(a => a.Id))
                sb.AppendLine($"A {a.Id} {a.Day:O} {a.Active}");
            foreach (var r in (await db.FactRetractions.AsNoTracking().ToListAsync()).OrderBy(r => r.Id))
                sb.AppendLine($"R {r.Id} {r.FactId} {r.Kind}");
            foreach (var e in (await db.SentEmailNotifications.AsNoTracking().ToListAsync()).OrderBy(e => e.Id))
                sb.AppendLine($"E {e.Id} {e.MedicineId} {e.StockEpoch} {e.EpochFactId} {e.SentAt:O}");
            foreach (var c in (await db.SyncConflicts.AsNoTracking().ToListAsync())
                         .Where(c => c.Kind is SyncConflictKind.MedicineField or SyncConflictKind.ScheduleSameDate
                             or SyncConflictKind.SlotSetReplaced)
                         .OrderBy(c => c.Id))
                sb.AppendLine($"X {c.Id} {c.Kind} {c.Register} {c.WinningValue} {c.LosingValue}");
            return sb.ToString();
        });
}
