namespace MedReminder.Application.Catalogue;

// What the database holds for one country of the reference catalogue:
// the imported snapshot version (null when the country is empty) and
// the number of reference medicines. The remote feed compares the
// version to decide whether to download, and uses the count to reject
// a snapshot that lost too many rows.
public sealed record CatalogueImportState(string? Version, int RowCount);
