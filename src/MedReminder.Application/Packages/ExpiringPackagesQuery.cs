using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;

namespace MedReminder.Application.Packages;

// A package that needs attention, with its medicine (docs/analysis/
// ANALYSIS-PACKAGE-EXPIRY.md §5.4, Stock → Expiring packages).
public sealed record ExpiringPackageItem(
    Guid MedicineId,
    string MedicineName,
    string Unit,
    bool MedicineIsActive,
    PackageListItem Item);

// The medicines with packages expired or expiring soon, and those
// packages. The one place that reads packages across medicines, used by
// the cabinet view and by the expiry notices, so both agree: inactive
// medicines included (their packages stay in the cabinet), the same
// allocation and lead days as the package list.
public sealed class ExpiringPackagesQuery
{
    private readonly IMedicineRepository _medicines;
    private readonly IStockPackageRepository _packages;
    private readonly IStockMovementRepository _stock;
    private readonly TimeProvider _clock;
    private readonly IProfileSettingsStore? _settings;

    public ExpiringPackagesQuery(IMedicineRepository medicines, IStockPackageRepository packages,
        IStockMovementRepository stock, TimeProvider clock, IProfileSettingsStore? settings = null)
    {
        _medicines = medicines;
        _packages = packages;
        _stock = stock;
        _clock = clock;
        _settings = settings;
    }

    // The cabinet view: expired first, then by effective expiry.
    public async Task<IReadOnlyList<ExpiringPackageItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime);
        var byMedicine = await LoadByMedicineAsync(today, include: null, cancellationToken);
        return [.. byMedicine
            .SelectMany(m => m.Items.Select(i =>
                new ExpiringPackageItem(m.Medicine.Id, m.Medicine.Name, m.Medicine.Unit, m.Medicine.IsActive, i)))
            .OrderBy(r => r.Item.Status)
            .ThenBy(r => r.Item.EffectiveExpiry)
            .ThenBy(r => r.MedicineName, StringComparer.CurrentCultureIgnoreCase)];
    }

    // Each medicine with at least one package needing attention on that
    // day, with those packages. include, when given, skips a medicine
    // before anything of it is read.
    public async Task<IReadOnlyList<(Medicine Medicine, IReadOnlyList<PackageListItem> Items)>> LoadByMedicineAsync(
        DateOnly today, Func<Medicine, bool>? include, CancellationToken cancellationToken)
    {
        // Read once, and only when some medicine has packages.
        PackageLeadDays? leadDays = null;
        var result = new List<(Medicine, IReadOnlyList<PackageListItem>)>();
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            if (include is not null && !include(medicine)) continue;
            var packages = await _packages.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (packages.Count == 0) continue;
            leadDays ??= PackageSettings.LeadDays(_settings);
            var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicine.Id, cancellationToken));
            var items = PackageListQuery.Build(packages, stock, today, leadDays).Where(i => i.NeedsAttention).ToList();
            if (items.Count > 0) result.Add((medicine, items));
        }
        return result;
    }
}
