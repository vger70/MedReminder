using MedReminder.Application.Abstractions;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Catalogue;

// Read-only lookups behind "Restock from barcode" (A2 phase 3, flow b):
// which medicines of the active profile carry a scanned national code,
// and which ones are not linked to any code yet. Writes nothing; the
// restock itself goes through AddStock, a link through
// LinkMedicineToReferenceUseCase.
// See docs/analysis/ANALYSIS-A2-BARCODE-SCAN.md §5C.
public sealed class RestockByScanQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;

    public RestockByScanQuery(IMedicineRepository medicines, IStockMovementRepository stock)
    {
        _medicines = medicines;
        _stock = stock;
    }

    // Medicines whose national code equals the scanned one, active and
    // inactive alike: restocking an inactive medicine is legitimate.
    // Each carries the quantity of its latest new package, the usual
    // size of the next one (§5C.2).
    public async Task<IReadOnlyList<RestockCandidate>> FindByNationalCodeAsync(
        string nationalCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nationalCode);
        var code = nationalCode.Trim();

        var all = await _medicines.ListAllAsync(cancellationToken);
        return await ToCandidatesAsync(
            all.Where(m => string.Equals(m.NationalCode?.Trim(), code, StringComparison.Ordinal)),
            cancellationToken);
    }

    // Medicines with no national code, which the user may link to a
    // scanned code that matched nothing.
    public async Task<IReadOnlyList<RestockCandidate>> ListUnlinkedAsync(CancellationToken cancellationToken)
    {
        var all = await _medicines.ListAllAsync(cancellationToken);
        return await ToCandidatesAsync(
            all.Where(m => string.IsNullOrWhiteSpace(m.NationalCode)), cancellationToken);
    }

    // Active medicines first, then by name.
    private async Task<IReadOnlyList<RestockCandidate>> ToCandidatesAsync(
        IEnumerable<Medicine> medicines,
        CancellationToken cancellationToken)
    {
        var ordered = medicines
            .OrderByDescending(m => m.IsActive)
            .ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var candidates = new List<RestockCandidate>(ordered.Count);
        foreach (var medicine in ordered)
        {
            var movements = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
            candidates.Add(new RestockCandidate(
                medicine.Id, medicine.Name, medicine.IsActive, LastNewPackageQuantity(movements)));
        }
        return candidates;
    }

    internal static decimal? LastNewPackageQuantity(IEnumerable<StockMovement> movements) =>
        movements
            .Where(m => m.Kind == StockMovementKind.NewPackage && m.QuantityDelta > 0m)
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => (decimal?)m.QuantityDelta)
            .FirstOrDefault();
}

public sealed record RestockCandidate(
    Guid MedicineId,
    string Name,
    bool IsActive,
    decimal? LastNewPackageQuantity);
