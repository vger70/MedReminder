using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Resolves a country's profile and the effective country scope its
// queries should hit. Sole owner of the "national ∪ EU" filter rule
// (ANALYSIS-DRUG-CATALOGUE.md §2.2 "Country filter, defined once in
// Application").
public interface ICountryProfileProvider
{
    CountryProfile GetProfile(CountryCode country);

    // Returns the ordered list of countries a query issued by
    // `userCountry` must scan. Order: user country first, then "EU"
    // when the profile includes centralised authorisations. When the
    // user country is itself "EU", the scope is only [ "EU" ].
    IReadOnlyList<CountryCode> GetSearchScope(CountryCode userCountry);
}
