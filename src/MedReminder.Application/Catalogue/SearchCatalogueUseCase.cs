using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Single façade the UI (M2) will call to autocomplete the medicine
// form. Owns the translation of the user's country into the effective
// search scope so that the port and every adapter stays neutral to
// the "national ∪ EU" rule.
public sealed class SearchCatalogueUseCase
{
    private readonly IReferenceCatalogueQueryService _query;
    private readonly ICountryProfileProvider _profiles;

    public const int DefaultLimit = 20;

    public SearchCatalogueUseCase(
        IReferenceCatalogueQueryService query,
        ICountryProfileProvider profiles)
    {
        _query = query;
        _profiles = profiles;
    }

    public Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
        string prefix,
        CountryCode userCountry,
        CancellationToken cancellationToken,
        int limit = DefaultLimit)
    {
        Validate(prefix, limit);
        var scope = _profiles.GetSearchScope(userCountry);
        return _query.SearchByCommercialNameAsync(prefix, scope, limit, cancellationToken);
    }

    public Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
        string prefix,
        CountryCode userCountry,
        CancellationToken cancellationToken,
        int limit = DefaultLimit)
    {
        Validate(prefix, limit);
        var scope = _profiles.GetSearchScope(userCountry);
        return _query.SearchByActiveIngredientAsync(prefix, scope, limit, cancellationToken);
    }

    private static void Validate(string prefix, int limit)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Search prefix cannot be empty.", nameof(prefix));
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }
    }
}
