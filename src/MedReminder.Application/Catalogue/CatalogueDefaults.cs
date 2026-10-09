using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Default reference catalogue for a reference country (decision DA3,
// docs/analysis/ANALYSIS-DESKTOP-COUNTRY-CATALOGUE-DEFAULTS.md §3.1 and
// ANALYSIS-B1-ANDROID-PLAN.md §4.4). Shared by the desktop and the
// Android app so that a profile gets the same default on both:
//   - the national catalogue when the country has one (a remote feed);
//   - otherwise the EMA (EU) catalogue when the country is in the
//     EU/EEA, where EMA centralised authorisations are valid;
//   - otherwise no catalogue: medicines are entered manually.
// The search scope of the returned catalogue is unchanged and comes
// from StaticCountryProfileProvider (national plus EU for IT, ES, FR;
// national only for US).
public static class CatalogueDefaults
{
    // The 27 EU member states plus Iceland, Liechtenstein and Norway
    // (decision DD3: EU/EEA, not the euro area). Greece is listed under
    // its ISO 3166-1 code GR; the EU's own "EL" abbreviation is not an
    // ISO code and is not accepted here.
    private static readonly HashSet<string> EuEeaCountries = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI",
        "FR", "GR", "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT",
        "NL", "PL", "PT", "RO", "SE", "SI", "SK",
        "IS", "LI", "NO",
    };

    // Catalogues the user can pick, in CatalogueFeedDescriptor.All order:
    // every country with a remote feed, plus EU.
    public static IReadOnlyList<CountryCode> AvailableCatalogues { get; } =
        CatalogueFeedDescriptor.All.Select(feed => feed.Country).ToList();

    // Whether `country` is an EU member state or an EEA country.
    public static bool IsEuEea(CountryCode country) => EuEeaCountries.Contains(country.Value);

    // Whether `catalogue` is one of AvailableCatalogues.
    public static bool IsAvailable(CountryCode catalogue) => AvailableCatalogues.Contains(catalogue);

    // Default catalogue for `referenceCountry`, or null for no catalogue.
    // A null or invalid country (region unknown, country step skipped)
    // gives no catalogue. "EU" as a country selects the EU catalogue.
    public static CountryCode? ForCountry(string? referenceCountry)
    {
        if (!CountryCode.TryParse(referenceCountry, out var country))
        {
            return null;
        }

        return ForCountry(country);
    }

    public static CountryCode? ForCountry(CountryCode country)
    {
        if (IsAvailable(country))
        {
            return country;
        }

        if (IsEuEea(country))
        {
            return CatalogueFeedDescriptor.EuropeanUnion.Country;
        }

        return null;
    }
}
