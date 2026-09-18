namespace MedReminder.Application.Catalogue;

// Feature flag for the reference catalogue.
//
// Off by default in M1: the DI registrations are wired so the query
// service and importer resolve, but no UI reads them yet. Toggling
// this to true is a no-op in M1 (no UI); M2 will gate the
// autocomplete on it.
public sealed class CatalogueFeatureOptions
{
    public const string SectionName = "Catalogue";

    public bool Enabled { get; set; }
}
