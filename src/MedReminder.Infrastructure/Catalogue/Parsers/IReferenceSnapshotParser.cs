using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parser strategy dispatched on the country the importer is running
// against. Implementations own the file format and the source-specific
// filtering rules (Omeopatico exclusion for AIFA, EU normalisation for
// EMA, etc.). Yielding is streaming so the importer can process large
// snapshots without loading everything into memory.
internal interface IReferenceSnapshotParser
{
    // Countries this parser handles. The importer dispatches by
    // matching against the expected country passed to ImportAsync.
    IReadOnlyCollection<CountryCode> SupportedCountries { get; }

    // Reads the snapshot stream and yields one row per commercial
    // product. Skipped source rows never appear in the output; the
    // parser tracks their count via ParseReport.
    IAsyncEnumerable<ReferenceMedicineRow> ParseAsync(
        Stream snapshot,
        ParseReport report,
        CancellationToken cancellationToken);
}

// Mutable counter passed by the importer to record how many source
// rows were dropped. Exposed as a class rather than returned so a
// streaming parser does not have to buffer everything before signalling
// its skip count.
internal sealed class ParseReport
{
    public int Skipped { get; private set; }

    public void RecordSkip(int count = 1) => Skipped += count;
}
