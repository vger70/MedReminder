using System.Globalization;
using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Sync;

namespace MedReminder.Application.Sync;

// Re-evaluates the stock counts of a medicine on the facts recorded
// before each of them, by HLC (B.1 Phase 3b-2, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §4.3 rule 3). On one device the result is
// the outcome stored when the count was recorded; after a merge it
// takes in the facts another device recorded earlier, so a count taken
// on the phone is right against what the desktop recorded before it.
//
// Snapshot of a count recorded at HLC t:
//   - facts recorded before t (the HLC of a fact is that of the
//     operation that recorded it); facts from before sync was enabled
//     have no operation and are always in;
//   - the therapy end date and each suspension's end date as they were
//     at t (register versions, SyncGenesis for the values before sync);
//   - the earlier counts, re-evaluated first.
// A retracted fact is not in any snapshot: its row is gone (product
// owner, 2026-09-27, §4.2).
//
// Counts recorded before sync was enabled keep their stored outcome and
// come first. Nothing changes while sync is disabled.
public sealed class CountReevaluation
{
    private readonly ISyncSettingsStore _settings;
    private readonly ISyncOperationRepository _operations;
    private readonly ISyncFieldVersionRepository _versions;
    private readonly TimeProvider _clock;

    public CountReevaluation(
        ISyncSettingsStore settings,
        ISyncOperationRepository operations,
        ISyncFieldVersionRepository versions,
        TimeProvider clock)
    {
        _settings = settings;
        _operations = operations;
        _versions = versions;
        _clock = clock;
    }

    public async Task<LedgerFacts> ApplyAsync(LedgerFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Counts.Count == 0 || _settings.Load() is null) return facts;

        var recordedAt = new Dictionary<Guid, HybridTimestamp>();
        foreach (var op in await _operations.ListForMedicineAsync(facts.MedicineId, cancellationToken))
        {
            if (op.EntityId is not { } id || op.Type is "SuspensionEndChanged" or "FactRetracted") continue;
            if (!recordedAt.TryGetValue(id, out var known) || op.Timestamp < known) recordedAt[id] = op.Timestamp;
        }

        var synced = facts.Counts.Where(c => recordedAt.ContainsKey(c.Id)).OrderBy(c => recordedAt[c.Id]).ToList();
        if (synced.Count == 0) return facts;
        var genesisCounts = facts.Counts.Where(c => !recordedAt.ContainsKey(c.Id)).ToList();

        var endDates = await VersionsAsync(facts.MedicineId, cancellationToken);
        var suspensionEnds = new Dictionary<Guid, IReadOnlyList<SyncFieldVersion>>();
        foreach (var s in facts.Suspensions)
        {
            suspensionEnds[s.Id] = await VersionsAsync(s.Id, cancellationToken);
        }

        var anchors = new List<StockCountAnchor>(genesisCounts);
        foreach (var count in synced)
        {
            var t = recordedAt[count.Id];
            bool Before(Guid id) => !recordedAt.TryGetValue(id, out var at) || at < t;

            var snapshot = facts with
            {
                EndDate = AsOf(endDates, t, facts.EndDate),
                UserEntries = [.. facts.UserEntries.Where(e => Before(e.Id))],
                Intakes = [.. facts.Intakes.Where(i => Before(i.Id))],
                Schedule = [.. facts.Schedule.Where(s => Before(s.Id))],
                SlotSets = [.. facts.SlotSets.Where(s => Before(s.Id))],
                Activity = [.. facts.Activity.Where(a => Before(a.Id))],
                Suspensions = [.. facts.Suspensions.Where(s => Before(s.Id)).Select(s => new MedicationSuspension
                {
                    Id = s.Id,
                    MedicineId = s.MedicineId,
                    StartDate = s.StartDate,
                    EndDate = AsOf(suspensionEnds[s.Id], t, s.EndDate),
                    Reason = s.Reason,
                    RecordedAt = s.RecordedAt,
                })],
                Counts = [.. anchors],
            };
            anchors.Add(LedgerDeriver.ReevaluateCount(snapshot, count, _clock.LocalTimeZone));
        }

        return facts with { Counts = anchors };
    }

    private async Task<IReadOnlyList<SyncFieldVersion>> VersionsAsync(Guid entityId, CancellationToken cancellationToken)
        => [.. (await _versions.ListForEntityAsync(entityId, cancellationToken))
            .Where(v => v.Register == SyncRegisters.EndDate)];

    // The register value in force just before `t`; the current value when
    // no version is older (the row did not exist yet, or sync started
    // without genesis versions).
    private static DateOnly? AsOf(IReadOnlyList<SyncFieldVersion> versions, HybridTimestamp t, DateOnly? current)
    {
        var winner = versions.Where(v => v.Version < t).MaxBy(v => v.Version);
        if (winner is null) return current;
        return winner.Value is null
            ? null
            : DateOnly.ParseExact(winner.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
