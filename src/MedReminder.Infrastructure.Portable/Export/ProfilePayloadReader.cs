using MedReminder.Application.Export;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedReminder.Infrastructure.Export;

// Reads every entity of a profile database into an archive payload
// (docs/analysis/ANALYSIS-C3-EXPORT-IMPORT.md §4.1 step 3), extracted
// from ExportService so that the Windows export and a mobile host
// produce the same payload. The caller passes a context over a snapshot
// of the database, never the live file.
internal static class ProfilePayloadReader
{
    public static async Task<ExportPayload> ReadAsync(
        MedReminderDbContext db, ExportedProfileInfo profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(profile);

        var payload = new ExportPayload { Profile = profile };

        // Ordered by Id so the produced payload is stable and diffable;
        // AsNoTracking because this context is read-only.
        payload.Medicines = (await db.Medicines.AsNoTracking()
            .OrderBy(m => m.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.StockMovements = (await db.StockMovements.AsNoTracking()
            .OrderBy(m => m.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationScheduleHistory = (await db.MedicationScheduleHistories.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationAdministrationSlots = (await db.MedicationAdministrationSlots.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationSuspensions = (await db.MedicationSuspensions.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationIntakes = (await db.MedicationIntakes.AsNoTracking()
            .OrderBy(i => i.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.NotificationEvents = (await db.NotificationEvents.AsNoTracking()
            .OrderBy(e => e.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.DoseReminderEvents = (await db.DoseReminderEvents.AsNoTracking()
            .OrderBy(e => e.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.MedicationAdministrationSlotSets = (await db.MedicationAdministrationSlotSets.AsNoTracking()
            .OrderBy(s => s.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.StockCounts = (await db.StockCounts.AsNoTracking()
            .OrderBy(c => c.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        var dispensations = (await db.PrescriptionDispensations.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(d => d.PrescriptionId);
        payload.Prescriptions = (await db.Prescriptions.AsNoTracking()
            .OrderBy(p => p.Id).ToListAsync(cancellationToken))
            .Select(p => ExportMapper.ToDto(p, dispensations[p.Id])).ToList();
        payload.SchemaVersion = ExportFormat.SchemaVersionOf(payload);
        payload.Deadlines = (await db.Deadlines.AsNoTracking()
            .OrderBy(d => d.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.StockPackages = (await db.StockPackages.AsNoTracking()
            .OrderBy(p => p.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.DoseTimePresets = (await db.DoseTimePresets.AsNoTracking()
            .OrderBy(p => p.Id).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        payload.DoseTimeDefaults = (await db.DoseTimeDefaults.AsNoTracking()
            .OrderBy(d => d.AdministrationsPerDay).ToListAsync(cancellationToken))
            .Select(ExportMapper.ToDto).ToList();
        var cutoff = await db.LedgerCutoffs.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        payload.LedgerCutoff = cutoff is null ? null : ExportMapper.ToDto(cutoff);

        return payload;
    }
}
