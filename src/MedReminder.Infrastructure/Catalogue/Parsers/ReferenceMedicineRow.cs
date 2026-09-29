using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Flattened row a parser yields for the importer to consume. One
// instance per commercial product per country; the active ingredients
// arrive already grouped so the importer does not have to
// re-aggregate them across CSV rows.
internal sealed record ReferenceMedicineRow(
    CountryCode Country,
    string NationalCode,
    string CommercialName,
    string? PharmaceuticalForm,
    string? Dosage,
    string? MarketingAuthorisationHolder,
    string? MarketingStatus,
    string? DispensingRegime,
    string? LinkLeaflet,
    string? LinkSpc,
    IReadOnlyList<ReferenceActiveIngredientRow> ActiveIngredients);

internal sealed record ReferenceActiveIngredientRow(
    string Name,
    AtcCode? Atc);
