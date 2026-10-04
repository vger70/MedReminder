using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Write side of the reference catalogue.
//
// The importer is transactional per (country, snapshot version):
//   - If the stored snapshot version is equal to or newer than the
//     candidate (SnapshotVersion.IsNewer), the import is a no-op
//     (Inserted = Updated = Deleted = 0) and the report carries the
//     stored version.
//   - Otherwise the importer replaces the country's rows in one
//     transaction, then reports the deltas.
//   - A snapshot that parses to fewer than `minimumRowCount` rows is
//     rejected with InvalidDataException before the transaction, so a
//     broken snapshot never empties the catalogue.
//
// `expectedCountry` gates parser strategy dispatch: the AIFA parser
// only accepts "IT", the EMA Article 57 parser only "EU", etc.
//
// Implementations take WriteGate (IDatabaseExclusiveAccess) for every
// database access, so callers must not hold it: the gate is not
// reentrant.
public interface IReferenceCatalogueImporter
{
    // Same as the overload below with minimumRowCount = 1.
    Task<ImportReport> ImportAsync(
        Stream snapshot,
        CountryCode expectedCountry,
        string snapshotVersion,
        CancellationToken cancellationToken);

    Task<ImportReport> ImportAsync(
        Stream snapshot,
        CountryCode expectedCountry,
        string snapshotVersion,
        int minimumRowCount,
        CancellationToken cancellationToken);

    Task<CatalogueImportState> GetImportStateAsync(
        CountryCode country,
        CancellationToken cancellationToken);
}
