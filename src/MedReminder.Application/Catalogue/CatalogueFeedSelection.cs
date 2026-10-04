using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Which remote feeds a client refreshes at startup (decision D4 of
// docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §5.3): the
// catalogue autocomplete reads the reference country plus EU, so only
// those two are fetched. Other countries stay at their embedded version
// until the user switches to them.
public static class CatalogueFeedSelection
{
    // Enabled feeds for `referenceCountry`, in CatalogueFeedDescriptor.All
    // order. An invalid value falls back to IT, as the autocomplete does
    // (MainForm.BuildCatalogueContext); a country without a feed (for
    // example DE) or EU itself selects EU only.
    public static IReadOnlyList<CatalogueFeedDescriptor> Select(string? referenceCountry, CatalogueFeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var reference = CountryCode.TryParse(referenceCountry, out var parsed)
            ? parsed
            : CatalogueFeedDescriptor.Italy.Country;

        return CatalogueFeedDescriptor.All
            .Where(feed => (feed.Country == reference || feed.Country.IsSupranational) && options.IsFeedEnabled(feed))
            .ToList();
    }

    // Whether the shortage list is refreshed with the feeds: it lists
    // Italian package codes, so only with Italy as reference country
    // (an invalid value falls back to IT, as above).
    public static bool IncludesShortages(string? referenceCountry, CatalogueFeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ShortagesEnabled && IsItaly(referenceCountry);
    }

    // Whether the equivalents list is refreshed with the feeds: it lists
    // Italian package codes, so under the same rule as the shortages.
    public static bool IncludesEquivalents(string? referenceCountry, CatalogueFeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.EquivalentsEnabled && IsItaly(referenceCountry);
    }

    // Whether the regional services list is refreshed with the feeds: it
    // names Italian regional services, so under the same rule.
    public static bool IncludesRegionalServices(string? referenceCountry, CatalogueFeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.RegionalServicesEnabled && IsItaly(referenceCountry);
    }

    // Italy as reference country. An invalid value falls back to IT, as
    // above.
    public static bool IsItaly(string? referenceCountry)
    {
        var reference = CountryCode.TryParse(referenceCountry, out var parsed)
            ? parsed
            : CatalogueFeedDescriptor.Italy.Country;
        return reference == CatalogueFeedDescriptor.Italy.Country;
    }
}
