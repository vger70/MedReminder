namespace MedReminder.Domain.Catalogue;

// A commercial medicinal product sourced from the reference catalogue
// (AIFA row in Italy, EMA Article 57 row for EU centralised
// authorisations). Represents the read model exposed to the query
// service: the persistence layer projects rows into instances of this
// type and never leaks its own row DTOs.
public sealed class ReferenceMedicine
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required CountryCode Country { get; init; }

    // "AIC" for IT, EMA product number for EU, national code for
    // other countries. Not necessarily numeric; stored as text.
    public required string NationalCode { get; init; }

    public required string CommercialName { get; init; }

    public string? PharmaceuticalForm { get; init; }

    public string? Dosage { get; init; }

    public string? MarketingAuthorisationHolder { get; init; }

    public string? MarketingStatus { get; init; }

    // Prescription-status label straight from the source data
    // (AIFA "FORNITURA"). Free-form text: the query service does not
    // filter by it in M1, but the UI badges withdrawn products.
    public string? DispensingRegime { get; init; }

    // URL of the package leaflet (AIFA "LINK_FI").
    public string? LinkLeaflet { get; init; }

    // URL of the summary of product characteristics (AIFA "LINK_RCP").
    public string? LinkSummaryOfProductCharacteristics { get; init; }

    // Snapshot version this row was imported from (e.g. "202609").
    // Used by the importer to detect stale rows on a newer snapshot.
    public required string SnapshotVersion { get; init; }

    public IReadOnlyList<ReferenceActiveIngredient> ActiveIngredients { get; init; }
        = Array.Empty<ReferenceActiveIngredient>();
}
