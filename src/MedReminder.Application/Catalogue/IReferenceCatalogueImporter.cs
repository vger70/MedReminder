using MedReminder.Domain.Catalogue;

namespace MedReminder.Application.Catalogue;

// Write side of the reference catalogue.
//
// The importer is transactional per (country, snapshot version):
//   - If the snapshot version already matches the rows in the DB,
//     the import is a no-op (Inserted = Updated = Deleted = 0).
//   - If the snapshot version differs, the importer replaces the
//     country's rows in one transaction, then reports the deltas.
//
// `expectedCountry` gates parser strategy dispatch: the AIFA parser
// only accepts "IT", the EMA Article 57 parser only "EU", etc.
public interface IReferenceCatalogueImporter
{
    Task<ImportReport> ImportAsync(
        Stream snapshot,
        CountryCode expectedCountry,
        string snapshotVersion,
        CancellationToken cancellationToken);
}
