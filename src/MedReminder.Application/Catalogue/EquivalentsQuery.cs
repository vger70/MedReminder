using MedReminder.Application.Abstractions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

public enum EquivalentsState
{
    // The medicine has no valid AIC: nothing to look up.
    NoCode,
    // No equivalents list is stored on this PC yet.
    NoList,
    // The package is not in the list. This does not mean that no
    // equivalent exists (patented, class C or simply not listed).
    NotListed,
    Listed,
}

// The equivalents of one package (ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK
// §2.6, uses U1 to U3). Rows are cheapest first.
public sealed record EquivalentsView(
    EquivalentsState State,
    string? NationalCode,
    DateOnly? ListDate,
    EquivalenceGroup? Group,
    IReadOnlyList<EquivalentRow> Rows,
    DateOnly? ShortageListDate)
{
    public EquivalentRow? Current => Rows.FirstOrDefault(r => r.IsCurrent);

    // The other packages of the group already in stock in the profile.
    public IReadOnlyList<EquivalentRow> AtHome => Rows.Where(r => !r.IsCurrent && r.AtHome.Count > 0).ToList();
}

// One package of the group. `Shortage` is its state in the stored
// shortage list (U2); `AtHome` the names of the profile's other
// medicines with this code and stock left (U3).
public sealed record EquivalentRow(
    EquivalentPackage Package,
    bool IsCurrent,
    ShortageNotice? Shortage,
    IReadOnlyList<string> AtHome);

// Joins a package code with the stored equivalents list, the shortage
// list and the profile's stock (§2.5): only through the AIC, never by
// name or active ingredient, since a wrong match on strength or form is
// a safety problem.
public sealed class EquivalentsQuery
{
    private readonly IEquivalenceListStore _equivalents;
    private readonly IMedicineRepository _medicines;
    private readonly IStockMovementRepository _stock;
    private readonly TimeProvider _clock;
    private readonly IShortageListStore? _shortages;

    public EquivalentsQuery(
        IEquivalenceListStore equivalents,
        IMedicineRepository medicines,
        IStockMovementRepository stock,
        TimeProvider clock,
        IShortageListStore? shortages = null)
    {
        _equivalents = equivalents;
        _medicines = medicines;
        _stock = stock;
        _clock = clock;
        _shortages = shortages;
    }

    // Whether the stored list has a group for the code; cheap (the store
    // caches the parsed file).
    public bool IsListed(string? nationalCode)
        => Normalize(nationalCode) is { } code && _equivalents.Load()?.FindGroup(code) is not null;

    // `medicineId` is the medicine the code belongs to, when saved: it
    // is not reported as an equivalent "at home" of itself.
    public async Task<EquivalentsView> LoadAsync(string? nationalCode, Guid? medicineId, CancellationToken cancellationToken)
    {
        if (Normalize(nationalCode) is not { } code)
            return new EquivalentsView(EquivalentsState.NoCode, null, null, null, [], null);
        if (_equivalents.Load() is not { } list)
            return new EquivalentsView(EquivalentsState.NoList, code, null, null, [], null);
        if (list.FindGroup(code) is not { } group)
            return new EquivalentsView(EquivalentsState.NotListed, code, list.ListDate, null, [], null);

        var atHome = await InStockByCodeAsync(group, medicineId, cancellationToken);
        var shortages = _shortages?.Load();
        var today = LocalToday();
        var rows = group.MembersByPrice()
            .Select(p => new EquivalentRow(
                p,
                string.Equals(p.Code, code, StringComparison.Ordinal),
                shortages?.NoticeFor(p.Code, today),
                atHome.TryGetValue(p.Code, out var names) ? names : []))
            .ToList();
        return new EquivalentsView(EquivalentsState.Listed, code, list.ListDate, group, rows, shortages?.ListDate);
    }

    // Names of the profile's medicines, other than `excluded`, whose code
    // is in the group and whose stock is above zero, by code. Inactive
    // medicines count: their packages are still at home.
    private async Task<Dictionary<string, IReadOnlyList<string>>> InStockByCodeAsync(
        EquivalenceGroup group, Guid? excluded, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var medicine in await _medicines.ListAllAsync(cancellationToken))
        {
            if (medicine.Id == excluded) continue;
            if (Normalize(medicine.NationalCode) is not { } code || !group.Contains(code)) continue;
            var movements = await _stock.ListForMedicineAsync(medicine.Id, cancellationToken);
            if (MedicineStock.Current(movements) <= 0m) continue;
            if (!found.TryGetValue(code, out var names))
            {
                names = new List<string>();
                found[code] = names;
            }
            names.Add(medicine.Name);
        }
        return found.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value, StringComparer.Ordinal);
    }

    // The code when it is a valid AIC (the field can also hold an EMA,
    // Spanish or French code).
    private static string? Normalize(string? nationalCode)
        => nationalCode?.Trim() is { } code && ItalianPharmacode.IsValidAic(code) ? code : null;

    private DateOnly LocalToday()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _clock.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
