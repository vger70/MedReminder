using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
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

// The cabinet view: the packages of every medicine of the profile that
// are expired or expiring soon, expired first, then by effective expiry.
// Inactive medicines are included, as in the expiry notices: their
// packages stay in the cabinet. Same allocation and lead days as the
// package list, so both agree.
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

    public async Task<IReadOnlyList<ExpiringPackageItem>> LoadAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone).DateTime);
        // Read once, and only when some medicine has packages.
        PackageLeadDays? leadDays = null;
        var result = new List<ExpiringPackageItem>();
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            var packages = await _packages.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (packages.Count == 0) continue;
            leadDays ??= PackageSettings.LeadDays(_settings);
            var stock = MedicineStock.Current(await _stock.ListForMedicineAsync(medicine.Id, cancellationToken));
            result.AddRange(PackageListQuery.Build(packages, stock, today, leadDays)
                .Where(i => i.NeedsAttention)
                .Select(i => new ExpiringPackageItem(medicine.Id, medicine.Name, medicine.Unit, medicine.IsActive, i)));
        }
        return [.. result
            .OrderBy(r => r.Item.Status)
            .ThenBy(r => r.Item.EffectiveExpiry)
            .ThenBy(r => r.MedicineName, StringComparer.CurrentCultureIgnoreCase)];
    }
}
