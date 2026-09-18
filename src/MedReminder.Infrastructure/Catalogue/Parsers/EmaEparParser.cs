using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parses the EMA EPAR (European public assessment reports) snapshot
// into ReferenceMedicineRow instances for the supranational EU
// catalogue (ANALYSIS-DRUG-CATALOGUE.md §3.4 M3).
//
// Input format: a ZIP archive with a single CSV entry named
// `ema-epar.csv` at the archive root, delimiter ';', UTF-8, header
// row present. The 39-column layout is the one EMA emits when the
// user exports the "Medicines" report from
// https://www.ema.europa.eu/en/medicines. Only the columns listed
// under ColumnNames are consumed; the rest are read past by the CSV
// splitter and ignored.
//
// Import filters applied here:
//   - Skip rows where `Category != 'Human'` — MedReminder is a
//     human-medicine reminder; veterinary rows are recorded as
//     Skipped and never surface.
//   - Every emitted row carries `country = 'EU'`, regardless of what
//     `Marketing authorisation holder` says. The MAH string is free
//     text and sometimes contains the substring "European Union";
//     that text lands in the MAH column, never in the country column.
//     `CountryCode.Parse("European Union")` already normalises to
//     "EU" and is used defensively for the country hard-coding.
//   - Active substances arrive as one string per row, with individual
//     substances separated by ';' (semicolon). Comma is NOT treated
//     as a separator: EMA descriptions routinely embed commas inside
//     a single substance description (e.g. HPV vaccine "types 6, 11,
//     16, 18, 31, 33, 45, 52, 58"). Substances are trimmed and
//     deduplicated per row; the row-level ATC is copied onto every
//     substance (same convention as AifaSnapshotParser, so
//     LinkMedicineToReferenceUseCase always finds an ATC when the
//     source row has one).
//   - `NationalCode` = EMA product number (e.g. `EMEA/H/C/004556`).
//     Every row carries a unique one, so `(country, national_code)`
//     stays UNIQUE without any synthetic derivation.
//   - `PharmaceuticalForm`, `Dosage`, `DispensingRegime`,
//     `LinkLeaflet`, `LinkSpc` are `null`: EPAR does not expose them
//     as structured columns. `LinkLeaflet` / `LinkSpc` are populated
//     from `Medicine URL` (the product's landing page on
//     ema.europa.eu, which links to leaflet + SPC).
internal sealed class EmaEparParser : IReferenceSnapshotParser
{
    private const string CsvEntryName = "ema-epar.csv";
    private const string HumanCategory = "Human";
    private const char ActiveSubstanceSeparator = ';';

    private static readonly CountryCode Eu = CountryCode.Parse("European Union");

    // Column names as they appear in the EMA EPAR export header. Matched
    // case-insensitively so a future EMA release that changes the casing
    // does not break the parser.
    private static class ColumnNames
    {
        public const string Category = "Category";
        public const string Name = "Name of medicine";
        public const string EmaProductNumber = "EMA product number";
        public const string MedicineStatus = "Medicine status";
        public const string ActiveSubstance = "Active substance";
        public const string AtcCodeHuman = "ATC code (human)";
        public const string MarketingAuthorisationHolder =
            "Marketing authorisation developer / applicant / holder";
        public const string MedicineUrl = "Medicine URL";
    }

    public IReadOnlyCollection<CountryCode> SupportedCountries { get; } = new[] { Eu };

    public async IAsyncEnumerable<ReferenceMedicineRow> ParseAsync(
        Stream snapshot,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(report);

        Stream working = snapshot.CanSeek ? snapshot : await BufferAsync(snapshot, cancellationToken);
        try
        {
            using var archive = new ZipArchive(working, ZipArchiveMode.Read, leaveOpen: true);
            var entry = FindEntry(archive, CsvEntryName);

            await foreach (var row in ReadRowsAsync(entry, report, cancellationToken))
            {
                yield return row;
            }
        }
        finally
        {
            if (!ReferenceEquals(working, snapshot))
            {
                await working.DisposeAsync();
            }
        }
    }

    private static ZipArchiveEntry FindEntry(ZipArchive archive, string logicalName)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.Name, logicalName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        throw new InvalidDataException(
            $"EMA EPAR snapshot is missing required entry '{logicalName}'.");
    }

    private static async IAsyncEnumerable<ReferenceMedicineRow> ReadRowsAsync(
        ZipArchiveEntry entry,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var header = await ReadHeaderAsync(reader, cancellationToken);
        var categoryIdx = RequireColumn(header, ColumnNames.Category);
        var nameIdx = RequireColumn(header, ColumnNames.Name);
        var emaNumIdx = RequireColumn(header, ColumnNames.EmaProductNumber);
        var statusIdx = RequireColumn(header, ColumnNames.MedicineStatus);
        var activeIdx = RequireColumn(header, ColumnNames.ActiveSubstance);
        var atcIdx = RequireColumn(header, ColumnNames.AtcCodeHuman);
        var mahIdx = RequireColumn(header, ColumnNames.MarketingAuthorisationHolder);
        var urlIdx = RequireColumn(header, ColumnNames.MedicineUrl);
        var maxIdx = Math.Max(
            Math.Max(Math.Max(categoryIdx, nameIdx), Math.Max(emaNumIdx, statusIdx)),
            Math.Max(Math.Max(activeIdx, atcIdx), Math.Max(mahIdx, urlIdx)));

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = CsvRow.Split(line);
            if (fields.Count <= maxIdx)
            {
                report.RecordSkip();
                continue;
            }

            // Filter veterinary rows: MedReminder is a human-medicine
            // reminder, and mixing veterinary products into the
            // autocomplete would confuse users. Comparison is
            // case-insensitive against the "Human" literal EMA uses.
            if (!string.Equals(fields[categoryIdx], HumanCategory, StringComparison.OrdinalIgnoreCase))
            {
                report.RecordSkip();
                continue;
            }

            var emaNumber = fields[emaNumIdx];
            if (string.IsNullOrWhiteSpace(emaNumber))
            {
                report.RecordSkip();
                continue;
            }

            var commercialName = fields[nameIdx];
            if (string.IsNullOrWhiteSpace(commercialName))
            {
                report.RecordSkip();
                continue;
            }

            var ingredients = BuildIngredients(fields[activeIdx], fields[atcIdx]);

            yield return new ReferenceMedicineRow(
                Country: Eu,
                NationalCode: emaNumber,
                CommercialName: commercialName,
                PharmaceuticalForm: null,
                Dosage: null,
                MarketingAuthorisationHolder: NullIfBlank(fields[mahIdx]),
                MarketingStatus: NullIfBlank(fields[statusIdx]),
                DispensingRegime: null,
                LinkLeaflet: NullIfBlank(fields[urlIdx]),
                LinkSpc: NullIfBlank(fields[urlIdx]),
                ActiveIngredients: ingredients);
        }
    }

    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(
        string rawActive, string rawAtc)
    {
        if (string.IsNullOrWhiteSpace(rawActive))
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var atc = AtcCode.TryParse(rawAtc, out var parsed) ? parsed : (AtcCode?)null;

        var parts = rawActive.Split(ActiveSubstanceSeparator, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ReferenceActiveIngredientRow>(capacity: parts.Length);
        foreach (var raw in parts)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }
            if (!seen.Add(trimmed))
            {
                continue;
            }
            rows.Add(new ReferenceActiveIngredientRow(trimmed, atc));
        }
        return rows;
    }

    private static async Task<Stream> BufferAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private static async Task<IReadOnlyList<string>> ReadHeaderAsync(
        StreamReader reader, CancellationToken cancellationToken)
    {
        var header = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidDataException("EMA EPAR CSV is empty.");
        return CsvRow.Split(header);
    }

    private static int RequireColumn(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (string.Equals(header[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        throw new InvalidDataException($"EMA EPAR CSV is missing required column '{name}'.");
    }

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
