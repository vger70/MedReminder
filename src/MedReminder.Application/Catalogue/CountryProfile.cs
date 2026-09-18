using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Per-country profile driving how the reference catalogue is queried.
//
// M1 exposes a single flag: IncludesEuCentralised. When true (the
// default for EU / EEA members) queries for that country also union
// EMA "EU" centralised rows. When false (e.g. UK GB after Brexit)
// queries return only that country's own rows. See ANALYSIS-DRUG-
// CATALOGUE.md §12 point 7.
public sealed record CountryProfile(CountryCode Country, bool IncludesEuCentralised);
