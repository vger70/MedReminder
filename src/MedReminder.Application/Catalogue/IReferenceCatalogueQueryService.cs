using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Read side of the reference catalogue.
//
// The signature deviates slightly from ANALYSIS-DRUG-CATALOGUE.md
// §2.2: instead of `CountryCode userCountry`, each search accepts an
// already-resolved country scope. This keeps the "national ∪ EU"
// rule owned by SearchCatalogueUseCase / ICountryProfileProvider and
// out of every adapter, and makes the union semantics observable
// through a fake port in Application-layer tests.
public interface IReferenceCatalogueQueryService
{
    // Prefix-search on the normalised commercial name. `countryScope`
    // is the list of countries the query must union (typically
    // [userCountry, "EU"] for EU-covered countries, [userCountry]
    // otherwise). Returns at most `limit` rows, ordered by exact
    // prefix match first then alphabetically.
    Task<IReadOnlyList<ReferenceMedicine>> SearchByCommercialNameAsync(
        string prefix,
        IReadOnlyCollection<CountryCode> countryScope,
        int limit,
        CancellationToken cancellationToken);

    // Prefix-search on the normalised active-ingredient name.
    Task<IReadOnlyList<ReferenceMedicine>> SearchByActiveIngredientAsync(
        string prefix,
        IReadOnlyCollection<CountryCode> countryScope,
        int limit,
        CancellationToken cancellationToken);

    // Exact lookup by (country, national code). Used by
    // LinkMedicineToReferenceUseCase when the UI selects a specific
    // catalogue row.
    Task<ReferenceMedicine?> GetByNationalCodeAsync(
        CountryCode country,
        string nationalCode,
        CancellationToken cancellationToken);

    // Distinct country codes with at least one row in the local
    // catalogue. Used by the Settings dialog to populate the
    // "Reference country" dropdown. Empty when no snapshot has been
    // imported yet — callers should still surface "IT" and "EU" as
    // fixed options.
    Task<IReadOnlyList<CountryCode>> ListAvailableCountriesAsync(
        CancellationToken cancellationToken);
}
