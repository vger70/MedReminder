using MedReminder.Domain.Catalogue;

namespace MedReminder.Domain.Prescriptions;

// The health record service of an Italian region or autonomous province
// where the issued electronic prescriptions are shown
// (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1): a web
// portal, and optionally the regional app. Public reference data checked
// by a person; MedReminder only opens WebUrl in the browser or shows an
// app link as a QR code. It never signs in and never reads the service.
public sealed record RegionalHealthService(
    string RegionCode,
    string Region,
    string Service,
    Uri WebUrl,
    Uri? IosAppUrl,
    Uri? AndroidAppUrl,
    IReadOnlyList<SignInMethod> SignIn,
    bool ShowsPrescriptions,
    bool FamilyDelegation,
    DateOnly VerifiedOn)
{
    // Every link of the entry: the only URLs MedReminder opens or shows.
    public IEnumerable<Uri> Links()
    {
        yield return WebUrl;
        if (IosAppUrl is not null) yield return IosAppUrl;
        if (AndroidAppUrl is not null) yield return AndroidAppUrl;
    }
}

// The sign-in methods a regional service accepts, as the list names them.
public enum SignInMethod
{
    Spid,
    Cie,
    TsCns,
}

// The list of regional services, one entry at most per region code.
public sealed class RegionalServicesList
{
    private readonly Dictionary<string, RegionalHealthService> _services;

    public RegionalServicesList(CountryCode country, DateOnly listDate, IEnumerable<RegionalHealthService> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Country = country;
        ListDate = listDate;
        _services = new Dictionary<string, RegionalHealthService>(StringComparer.Ordinal);
        foreach (var service in services) _services[service.RegionCode] = service;
    }

    public CountryCode Country { get; }

    // The date the list was published.
    public DateOnly ListDate { get; }

    public int Count => _services.Count;

    public IEnumerable<RegionalHealthService> Services => _services.Values;

    public RegionalHealthService? Find(string? regionCode)
        => regionCode is not null && _services.TryGetValue(regionCode.Trim(), out var service) ? service : null;
}

// The Italian regions and autonomous provinces, by ISTAT region code.
// Trentino-Alto Adige (04) is replaced by its two autonomous provinces,
// each with its own health service: 21 Bolzano and 22 Trento, as in the
// national open data that split the region. The names shown to the user
// come from the localization files ("Regions.IT.<code>").
public static class ItalianRegions
{
    public static readonly IReadOnlyList<string> Codes =
    [
        "01", "02", "03", "05", "06", "07", "08", "09", "10", "11",
        "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22",
    ];

    public static bool IsValid(string? code) => code is not null && Codes.Contains(code, StringComparer.Ordinal);
}
