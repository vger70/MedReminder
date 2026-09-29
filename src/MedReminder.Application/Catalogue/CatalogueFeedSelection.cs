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
}
