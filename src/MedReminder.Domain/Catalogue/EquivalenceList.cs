namespace MedReminder.Domain.Catalogue;

// A national list of equivalent medicines (the Italian AIFA "Lista di
// trasparenza"; docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md
// §2), keyed on the package code (the 9-digit AIC). Packages of one
// group are interchangeable for the national health service within the
// limits of each package's note. Public information only: it makes no
// clinical statement and says nothing about excipients or allergies.
public sealed class EquivalenceList
{
    private readonly Dictionary<string, EquivalenceGroup> _groups;
    private readonly Dictionary<string, EquivalenceGroup> _groupOfPackage;

    public EquivalenceList(CountryCode country, DateOnly listDate, IEnumerable<EquivalenceGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Country = country;
        ListDate = listDate;
        _groups = new Dictionary<string, EquivalenceGroup>(StringComparer.Ordinal);
        _groupOfPackage = new Dictionary<string, EquivalenceGroup>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            _groups[group.Code] = group;
            // A code belongs to one group only; the feed script rejects a
            // list that breaks this, the first group wins otherwise.
            foreach (var member in group.Members) _groupOfPackage.TryAdd(member.Code, group);
        }
    }

    public CountryCode Country { get; }

    // The date the publisher gives the list.
    public DateOnly ListDate { get; }

    public int GroupCount => _groups.Count;

    public int PackageCount => _groupOfPackage.Count;

    public EquivalenceGroup? FindGroup(string? code)
        => code is not null && _groupOfPackage.TryGetValue(code.Trim(), out var group) ? group : null;

    public EquivalenceGroup? GroupByCode(string? groupCode)
        => groupCode is not null && _groups.TryGetValue(groupCode, out var group) ? group : null;
}

// One equivalence group: the packages that share active ingredient,
// strength, form, route and number of units. `Reference` is the
// publisher's normalised description of the group ("28 UNITA' 5 MG -
// USO ORALE"); prices are in euros, null when not published.
public sealed class EquivalenceGroup
{
    public EquivalenceGroup(
        string code,
        string activeIngredient,
        string reference,
        string? atcCode,
        decimal? referencePrice,
        IEnumerable<EquivalentPackage> members)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(members);
        Code = code;
        ActiveIngredient = activeIngredient ?? string.Empty;
        Reference = reference ?? string.Empty;
        AtcCode = atcCode;
        ReferencePrice = referencePrice;
        Members = members.ToList();
    }

    public string Code { get; }

    public string ActiveIngredient { get; }

    public string Reference { get; }

    public string? AtcCode { get; }

    public decimal? ReferencePrice { get; }

    public IReadOnlyList<EquivalentPackage> Members { get; }

    // Cheapest first; packages without a price last; then by name and
    // code so the order is stable.
    public IReadOnlyList<EquivalentPackage> MembersByPrice()
        => Members
            .OrderBy(m => m.PublicPrice is null)
            .ThenBy(m => m.PublicPrice)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Code, StringComparer.Ordinal)
            .ToList();

    public bool Contains(string? code)
        => code is not null && Members.Any(m => string.Equals(m.Code, code.Trim(), StringComparison.Ordinal));
}

// A package of a group. `Difference` is what the patient pays on top of
// the reference price for this package; `Note` is the publisher's text,
// kept verbatim: it can restrict substitution inside the group.
public sealed record EquivalentPackage(
    string Code,
    string Name,
    string Package,
    string Holder,
    decimal? PublicPrice,
    decimal? Difference,
    string? Note)
{
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}
