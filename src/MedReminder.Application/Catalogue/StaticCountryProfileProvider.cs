using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Static country-profile lookup shipped with the code. Chosen over a
// DB table for M1 (no separate country_profiles table) since the
// per-country flag is stable enough to live in source. A DB-backed
// implementation of ICountryProfileProvider can replace it later
// without touching callers.
//
// Defaults:
//   - Any country not explicitly listed → IncludesEuCentralised = true.
//   - "EU" → IncludesEuCentralised = false (querying "EU" already
//     means "supranational rows only").
//   - Explicit opt-outs (§12 point 7): "GB", "UK".
public sealed class StaticCountryProfileProvider : ICountryProfileProvider
{
    private static readonly HashSet<string> NonEuCovered = new(StringComparer.Ordinal)
    {
        "GB",
        "UK",
    };

    public CountryProfile GetProfile(CountryCode country)
    {
        if (country.IsSupranational)
        {
            return new CountryProfile(country, IncludesEuCentralised: false);
        }

        var includesEu = !NonEuCovered.Contains(country.Value);
        return new CountryProfile(country, includesEu);
    }

    public IReadOnlyList<CountryCode> GetSearchScope(CountryCode userCountry)
    {
        var profile = GetProfile(userCountry);

        if (userCountry.IsSupranational)
        {
            return new[] { userCountry };
        }

        if (profile.IncludesEuCentralised)
        {
            return new[] { userCountry, CountryCode.Parse("EU") };
        }

        return new[] { userCountry };
    }
}
