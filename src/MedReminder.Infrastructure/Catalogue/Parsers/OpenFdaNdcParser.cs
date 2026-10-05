using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parses the US catalogue snapshot built by scripts/feeds/fda_ndc.py
// from the FDA National Drug Code Directory, as published by openFDA
// under CC0 1.0 (docs/analysis/ANALYSIS-CATALOGUE-US-GB-SOURCES.md
// §3.2, §3.3).
//
// Input format: a ZIP archive with a single `fda-ndc.tsv` entry, TAB
// delimiter, UTF-8, header row, no quoting (the script replaces TAB and
// line breaks inside values with spaces). Columns are matched by name,
// case-insensitively; extra columns are ignored.
//
// One row per package NDC, as the Italian catalogue keys on the AIC
// package code: it is what the box carries.
//   - `NationalCode` = the package NDC in the 12-digit 6-4-2 form of
//     the FDA rule effective 2033-03-07 (segments left-padded), stable
//     across that change. Rows without that shape are skipped.
//   - `CommercialName` = brand name, or the generic name when the
//     product has none (the script already applies the fallback).
//   - `Dosage` = ingredient strengths and the package description.
//   - `MarketingStatus` = marketing category, or "Discontinued (…)"
//     when marketing ended before the export.
//   - `DispensingRegime` = Rx / OTC, plus the DEA schedule.
//   - `LinkLeaflet` = the DailyMed label page, built here from the SPL
//     set id only when it parses as a GUID, so nothing read from the
//     file reaches the URL unchecked. `LinkSpc` is null.
//   - Active ingredients `|`-separated; ATC is always null: the NDC
//     Directory carries FDA pharmacologic classes, not ATC.
internal sealed class OpenFdaNdcParser : IReferenceSnapshotParser
{
    private const string EntryName = "fda-ndc.tsv";
    private const char IngredientSeparator = '|';
    private const string DailyMedLabelBase = "https://dailymed.nlm.nih.gov/dailymed/drugInfo.cfm?setid=";

    private static readonly CountryCode UnitedStates = CountryCode.Parse("US");

    private static readonly Regex CanonicalNdc = new(@"^\d{6}-\d{4}-\d{2}$", RegexOptions.CultureInvariant);

    private static class ColumnNames
    {
        public const string Ndc = "ndc";
        public const string Name = "name";
        public const string Form = "form";
        public const string Dosage = "dosage";
        public const string Labeler = "labeler";
        public const string Status = "status";
        public const string Regime = "regime";
        public const string Ingredients = "ingredients";
        public const string SplSetId = "spl_set_id";
    }

    public IReadOnlyCollection<CountryCode> SupportedCountries { get; } = new[] { UnitedStates };

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
            var entry = FindEntry(archive, EntryName);

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
            $"FDA NDC snapshot is missing required entry '{logicalName}'.");
    }

    private static async IAsyncEnumerable<ReferenceMedicineRow> ReadRowsAsync(
        ZipArchiveEntry entry,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidDataException($"FDA NDC snapshot entry '{EntryName}' is empty.");
        }

        var header = headerLine.Split('\t');
        var ndcIdx = RequireColumn(header, ColumnNames.Ndc);
        var nameIdx = RequireColumn(header, ColumnNames.Name);
        var formIdx = RequireColumn(header, ColumnNames.Form);
        var dosageIdx = RequireColumn(header, ColumnNames.Dosage);
        var labelerIdx = RequireColumn(header, ColumnNames.Labeler);
        var statusIdx = RequireColumn(header, ColumnNames.Status);
        var regimeIdx = RequireColumn(header, ColumnNames.Regime);
        var ingredientsIdx = RequireColumn(header, ColumnNames.Ingredients);
        var setIdIdx = RequireColumn(header, ColumnNames.SplSetId);
        var minColumns = new[] { ndcIdx, nameIdx, formIdx, dosageIdx, labelerIdx, statusIdx, regimeIdx, ingredientsIdx, setIdIdx }.Max() + 1;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < minColumns)
            {
                report.RecordSkip();
                continue;
            }

            var ndc = fields[ndcIdx].Trim();
            var name = fields[nameIdx].Trim();
            if (!CanonicalNdc.IsMatch(ndc) || name.Length == 0)
            {
                report.RecordSkip();
                continue;
            }

            yield return new ReferenceMedicineRow(
                Country: UnitedStates,
                NationalCode: ndc,
                CommercialName: name,
                PharmaceuticalForm: NullIfBlank(fields[formIdx]),
                Dosage: NullIfBlank(fields[dosageIdx]),
                MarketingAuthorisationHolder: NullIfBlank(fields[labelerIdx]),
                MarketingStatus: NullIfBlank(fields[statusIdx]),
                DispensingRegime: NullIfBlank(fields[regimeIdx]),
                LinkLeaflet: DailyMedLink(fields[setIdIdx]),
                LinkSpc: null,
                ActiveIngredients: BuildIngredients(fields[ingredientsIdx]));
        }
    }

    // The label page of an SPL set id; null unless the id is a GUID.
    internal static string? DailyMedLink(string setId) =>
        Guid.TryParseExact(setId.Trim(), "D", out var id)
            ? DailyMedLabelBase + id.ToString("D")
            : null;

    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(string value)
    {
        var names = value
            .Split(IngredientSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var rows = new ReferenceActiveIngredientRow[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            rows[i] = new ReferenceActiveIngredientRow(names[i], Atc: null);
        }
        return rows;
    }

    private static int RequireColumn(string[] header, string name)
    {
        for (var i = 0; i < header.Length; i++)
        {
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        throw new InvalidDataException(
            $"FDA NDC snapshot '{EntryName}' is missing required column '{name}'.");
    }

    private static async Task<Stream> BufferAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
