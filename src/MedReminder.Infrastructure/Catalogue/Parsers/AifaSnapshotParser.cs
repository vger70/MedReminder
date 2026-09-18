using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parses an AIFA open-data snapshot into ReferenceMedicineRow
// instances for the Italian catalogue (ANALYSIS-DRUG-CATALOGUE.md
// §3.2 "AIFA import rules").
//
// Input format: a ZIP archive with two CSV entries side by side —
// `confezioni_fornitura.csv` (one row per package) and
// `PA_confezioni.csv` (one row per package × active ingredient).
// Delimiter ';', ASCII, `CODICE_AIC` a 9-digit text string kept with
// its leading zeros. Both files may be quoted or bare.
//
// Import filters applied here:
//   - Skip `TIPO_PROCEDURA = 'Omeopatico'` (recorded as Skipped).
//   - Skip `PRINCIPIO_ATTIVO = 'N.D.'` in PA_confezioni (dropped
//     from the ingredient list; not counted as a medicine skip).
//   - Every emitted row carries `country = 'IT'` regardless of
//     `TIPO_PROCEDURA` (Procedura Centralizzata rows are Italian-
//     market entries; the EU catalogue lands in M3).
//   - `dispensing_regime` ← FORNITURA, `link_leaflet` ← LINK_FI,
//     `link_spc` ← LINK_RCP.
internal sealed class AifaSnapshotParser : IReferenceSnapshotParser
{
    private const string ConfezioniEntryName = "confezioni_fornitura.csv";
    private const string PaEntryName = "PA_confezioni.csv";
    private const string OmeopaticoLabel = "Omeopatico";
    private const string NotAvailableLabel = "N.D.";

    private static readonly CountryCode Italy = CountryCode.Parse("IT");

    public IReadOnlyCollection<CountryCode> SupportedCountries { get; } = new[] { Italy };

    public async IAsyncEnumerable<ReferenceMedicineRow> ParseAsync(
        Stream snapshot,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(report);

        // ZipArchive requires seekable streams; the caller may pass a
        // MemoryStream (tests) or an assembly resource (M2). If the
        // stream is not seekable, buffer it once.
        Stream working = snapshot.CanSeek ? snapshot : await BufferAsync(snapshot, cancellationToken);
        try
        {
            using var archive = new ZipArchive(working, ZipArchiveMode.Read, leaveOpen: true);

            var paEntry = FindEntry(archive, PaEntryName);
            var confezioniEntry = FindEntry(archive, ConfezioniEntryName);

            var ingredientsByAic = await ReadIngredientsAsync(paEntry, cancellationToken);

            await foreach (var row in ReadMedicinesAsync(confezioniEntry, ingredientsByAic, report, cancellationToken))
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
            $"AIFA snapshot is missing required entry '{logicalName}'.");
    }

    private static async Task<Dictionary<string, List<string>>> ReadIngredientsAsync(
        ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var byAic = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var header = await ReadHeaderAsync(reader, cancellationToken);
        var aicIdx = RequireColumn(header, "CODICE_AIC");
        var paIdx = RequireColumn(header, "PRINCIPIO_ATTIVO");

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = CsvRow.Split(line);
            if (fields.Count <= Math.Max(aicIdx, paIdx))
            {
                continue;
            }

            var aic = fields[aicIdx];
            var ingredient = fields[paIdx];
            if (string.IsNullOrWhiteSpace(aic) || string.IsNullOrWhiteSpace(ingredient))
            {
                continue;
            }
            if (string.Equals(ingredient, NotAvailableLabel, StringComparison.Ordinal))
            {
                continue;
            }

            if (!byAic.TryGetValue(aic, out var list))
            {
                list = new List<string>(capacity: 2);
                byAic[aic] = list;
            }

            // Deduplicate while preserving the source order.
            if (!list.Contains(ingredient, StringComparer.Ordinal))
            {
                list.Add(ingredient);
            }
        }

        return byAic;
    }

    private static async IAsyncEnumerable<ReferenceMedicineRow> ReadMedicinesAsync(
        ZipArchiveEntry entry,
        Dictionary<string, List<string>> ingredientsByAic,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var header = await ReadHeaderAsync(reader, cancellationToken);
        var aicIdx = RequireColumn(header, "CODICE_AIC");
        var denomIdx = RequireColumn(header, "DENOMINAZIONE");
        var descIdx = RequireColumn(header, "DESCRIZIONE");
        var mahIdx = RequireColumn(header, "RAGIONE_SOCIALE");
        var statusIdx = RequireColumn(header, "STATO_AMMINISTRATIVO");
        var procIdx = RequireColumn(header, "TIPO_PROCEDURA");
        var formaIdx = RequireColumn(header, "FORMA");
        var atcIdx = RequireColumn(header, "CODICE_ATC");
        var fornituraIdx = RequireColumn(header, "FORNITURA");
        var linkFiIdx = RequireColumn(header, "LINK_FI");
        var linkRcpIdx = RequireColumn(header, "LINK_RCP");

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = CsvRow.Split(line);
            if (fields.Count <= linkRcpIdx)
            {
                report.RecordSkip();
                continue;
            }

            if (string.Equals(fields[procIdx], OmeopaticoLabel, StringComparison.Ordinal))
            {
                report.RecordSkip();
                continue;
            }

            var aic = fields[aicIdx];
            if (string.IsNullOrWhiteSpace(aic))
            {
                report.RecordSkip();
                continue;
            }

            var commercialName = fields[denomIdx];
            if (string.IsNullOrWhiteSpace(commercialName))
            {
                report.RecordSkip();
                continue;
            }

            ingredientsByAic.TryGetValue(aic, out var ingredientNames);
            var ingredients = BuildIngredients(ingredientNames, fields[atcIdx]);

            yield return new ReferenceMedicineRow(
                Country: Italy,
                NationalCode: aic,
                CommercialName: commercialName,
                PharmaceuticalForm: NullIfBlank(fields[formaIdx]),
                Dosage: NullIfBlank(fields[descIdx]),
                MarketingAuthorisationHolder: NullIfBlank(fields[mahIdx]),
                MarketingStatus: NullIfBlank(fields[statusIdx]),
                DispensingRegime: NullIfBlank(fields[fornituraIdx]),
                LinkLeaflet: NullIfBlank(fields[linkFiIdx]),
                LinkSpc: NullIfBlank(fields[linkRcpIdx]),
                ActiveIngredients: ingredients);
        }
    }

    // The medicine-level CODICE_ATC represents the substance
    // (single-ingredient) or the combination (multi-ingredient).
    // Attaching a combo ATC to each ingredient would be semantically
    // wrong, so ATC is copied onto the ingredient only for single-
    // ingredient medicines.
    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(
        List<string>? names, string rawAtc)
    {
        if (names is null || names.Count == 0)
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var atc = names.Count == 1 && AtcCode.TryParse(rawAtc, out var parsed)
            ? parsed
            : (AtcCode?)null;

        var rows = new ReferenceActiveIngredientRow[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            rows[i] = new ReferenceActiveIngredientRow(names[i], i == 0 ? atc : null);
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
            ?? throw new InvalidDataException("AIFA CSV is empty.");
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
        throw new InvalidDataException($"AIFA CSV is missing required column '{name}'.");
    }

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

// Small permissive splitter for a single CSV line. Handles ';' as
// delimiter, optional double-quoted fields, and doubled quotes inside
// a quoted field ("" → "). AIFA rows are single-line so there is no
// need to carry state across lines.
internal static class CsvRow
{
    public static IReadOnlyList<string> Split(string line)
    {
        var result = new List<string>(capacity: 16);
        var buffer = new StringBuilder(line.Length);
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        buffer.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    buffer.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case ';':
                    result.Add(buffer.ToString());
                    buffer.Clear();
                    break;
                case '"':
                    inQuotes = true;
                    break;
                default:
                    buffer.Append(ch);
                    break;
            }
        }

        result.Add(buffer.ToString());
        return result;
    }
}
