using MedReminder.Application.Abstractions;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Ledger;

// Why a fact cannot be retracted (B.1 Phase 2d).
public enum RetractionBlock
{
    None = 0,
    // Recorded before the ledger freeze: fixed with a correction, as
    // before (ANALYSIS-B1-MOBILE-SYNC.md §4.2).
    Legacy = 1,
    // A stock count was recorded after it: the count already absorbed
    // the mistake; a new count fixes the stock.
    LaterCount = 2,
}

// One fact of a medicine as the history window shows it. The fields
// that do not apply to Kind are null.
public sealed record FactHistoryItem(
    FactKind Kind,
    Guid FactId,
    DateOnly Day,
    DateTimeOffset RecordedAt,
    StockMovementKind? MovementKind,
    IntakeStatus? IntakeStatus,
    decimal? Quantity,
    decimal? Correction,
    DateOnly? EndDate,
    string? Notes,
    RetractionBlock Block)
{
    public bool CanRetract => Block == RetractionBlock.None;
}

// The facts a user entered for a medicine, newest first, with whether
// each one can be retracted (B.1 Phase 2d, D8). Rule (product owner,
// 2026-09-27): only facts recorded after the ledger freeze and after the
// medicine's latest stock count. Derived and Legacy stock rows are not
// facts and are not listed.
public sealed class FactHistoryQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly IMedicationIntakeRepository _intakes;
    private readonly IStockCountRepository _counts;
    private readonly IMedicationSuspensionRepository _suspensions;
    private readonly ILedgerCutoffRepository _cutoff;
    private readonly TimeProvider _clock;

    public FactHistoryQuery(
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        IMedicationIntakeRepository intakes,
        IStockCountRepository counts,
        IMedicationSuspensionRepository suspensions,
        ILedgerCutoffRepository cutoff,
        TimeProvider clock)
    {
        _clock = clock;
        _medicines = medicines;
        _stock = stock;
        _intakes = intakes;
        _counts = counts;
        _suspensions = suspensions;
        _cutoff = cutoff;
    }

    public async Task<IReadOnlyList<FactHistoryItem>> LoadAsync(Guid medicineId, CancellationToken cancellationToken)
    {
        var medicine = await _medicines.GetAsync(medicineId, cancellationToken)
            ?? throw new InvalidOperationException($"Medicine {medicineId} not found.");
        var frozenAt = (await _cutoff.GetAsync(cancellationToken))?.FrozenAt;

        bool IsLegacy(DateTimeOffset recordedAt) => frozenAt is { } f && recordedAt < f;

        var counts = (await _counts.ListForMedicineAsync(medicineId, cancellationToken))
            .Where(c => !IsLegacy(c.RecordedAt))
            .ToList();
        var latestCount = counts.Count == 0 ? (DateTimeOffset?)null : counts.Max(c => c.RecordedAt);

        RetractionBlock Block(DateTimeOffset recordedAt, bool legacy)
        {
            if (legacy) return RetractionBlock.Legacy;
            if (latestCount is { } last && last > recordedAt) return RetractionBlock.LaterCount;
            return RetractionBlock.None;
        }

        var items = new List<FactHistoryItem>();

        foreach (var m in await _stock.ListForMedicineAsync(medicineId, cancellationToken))
        {
            if (m.Origin != StockMovementOrigin.User) continue;
            var recordedAt = RecordedAt(medicine, m);
            items.Add(new FactHistoryItem(
                FactKind.StockEntry, m.Id,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.OccurredAt, _clock.LocalTimeZone).DateTime), recordedAt,
                m.Kind, null, m.QuantityDelta, null, null, m.Notes,
                Block(recordedAt, legacy: false)));
        }

        foreach (var i in await _intakes.ListForMedicineAsync(medicineId, cancellationToken))
        {
            items.Add(new FactHistoryItem(
                FactKind.Intake, i.Id, i.Day, i.RecordedAt,
                null, i.Status, i.Quantity, null, null, i.Notes,
                Block(i.RecordedAt, IsLegacy(i.RecordedAt))));
        }

        foreach (var c in counts)
        {
            items.Add(new FactHistoryItem(
                FactKind.StockCount, c.Id, c.CountDay, c.RecordedAt,
                null, null, c.CountedQuantity, c.Correction, null, c.Notes,
                Block(c.RecordedAt, legacy: false)));
        }

        foreach (var s in await _suspensions.ListForMedicineAsync(medicineId, cancellationToken))
        {
            items.Add(new FactHistoryItem(
                FactKind.Suspension, s.Id, s.StartDate, s.RecordedAt,
                null, null, null, null, s.EndDate, s.Reason,
                Block(s.RecordedAt, IsLegacy(s.RecordedAt))));
        }

        return items
            .OrderByDescending(i => i.RecordedAt)
            .ThenByDescending(i => i.Day)
            .ToList();
    }

    // The initial load is recorded with the medicine; its OccurredAt is
    // the start date, not the recording instant. Other entries are
    // recorded when they occur.
    private static DateTimeOffset RecordedAt(Medicine medicine, StockMovement entry)
        => entry.Kind == StockMovementKind.InitialLoad ? medicine.CreatedAt : entry.OccurredAt;
}
