using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parses an AEMPS / CIMA snapshot into ReferenceMedicineRow instances
// for the Spanish catalogue (ANALYSIS-DRUG-CATALOGUE.md §3.5 M4).
//
// Input format: a ZIP archive containing a single Office Open XML
// spreadsheet at the archive root, `aemps.xlsx`. The workbook has one
// sheet (Hoja1) with a fixed 15-column layout (verified against the
// current CIMA "Medicamentos" export):
//
//    0  Nº Registro            → NationalCode
//    1  Medicamento            → CommercialName (includes form + dose)
//    2  Laboratorio            → MarketingAuthorisationHolder
//    3  Fecha Aut.
//    4  Estado                 → MarketingStatus (Autorizado /
//                                Anulado / Suspenso)
//    5  Fecha Estado
//    6  Cód. ATC               → per-row ATC, copied onto every
//                                ingredient (pattern shared with
//                                AifaSnapshotParser and EmaEparParser)
//    7  Principios Activos     → active ingredients, joined by ", "
//    8  Nº P. Activos
//    9  ¿Comercializado?
//   10  ¿Triangulo Amarillo?
//   11  Observaciones          → DispensingRegime (free-text — e.g.
//                                "Medicamento Sujeto A Prescripción
//                                Médica", "Uso Hospitalario",
//                                "Diagnóstico Hospitalario",
//                                "Biológicos")
//   12  ¿Sustituible?
//   13  ¿Afecta conducción?
//   14  ¿Problemas de suministro?
//
// PharmaceuticalForm, Dosage, LinkLeaflet and LinkSpc are set to null:
// the CIMA XLSX embeds form and dose inside the CommercialName text
// (the same design AifaSnapshotParser sees for DENOMINAZIONE) and does
// not expose leaflet / SPC URLs as structured columns. The XML variant
// of the same export ships those fields separately — a future M5+
// increment can switch parser and lift them out, without changing the
// row schema.
//
// Every emitted row carries `country = 'ES'`. No veterinary filter is
// applied: the CIMA "Medicamentos" register covers human medicines
// only (veterinary ones live in the separate CIMAvet portal).
//
// Ingredient splitting: the "Principios Activos" column is a
// comma-space (", ") separated list. Substance names in CIMA never
// embed comma-space themselves (short chemical names), so a plain
// split is safe. Each substance is trimmed and deduplicated per row.
internal sealed class AempsCimaParser : IReferenceSnapshotParser
{
    private const string XlsxEntryName = "aemps.xlsx";
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string SharedStringsEntry = "xl/sharedStrings.xml";
    private const string SheetEntry = "xl/worksheets/sheet1.xml";
    private const string ActiveIngredientSeparator = ", ";

    private const int Col_NRegistro = 0;
    private const int Col_Medicamento = 1;
    private const int Col_Laboratorio = 2;
    private const int Col_Estado = 4;
    private const int Col_Atc = 6;
    private const int Col_PrincipiosActivos = 7;
    private const int Col_Observaciones = 11;
    private const int Col_Count = 15;

    private static readonly CountryCode Spain = CountryCode.Parse("ES");

    public IReadOnlyCollection<CountryCode> SupportedCountries { get; } = new[] { Spain };

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
            using var outer = new ZipArchive(working, ZipArchiveMode.Read, leaveOpen: true);
            var xlsxEntry = FindEntry(outer, XlsxEntryName);

            // The inner .xlsx is itself a ZIP; ZipArchive needs a
            // seekable stream, and the outer entry stream is not
            // seekable, so buffer it once.
            await using var xlsxBuffer = await BufferAsync(xlsxEntry.Open(), cancellationToken);
            using var xlsx = new ZipArchive(xlsxBuffer, ZipArchiveMode.Read, leaveOpen: true);

            var sharedStrings = await ReadSharedStringsAsync(xlsx, cancellationToken);
            var sheet = FindZipEntry(xlsx, SheetEntry);

            await foreach (var row in ReadRowsAsync(sheet, sharedStrings, report, cancellationToken))
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
            $"AEMPS snapshot is missing required entry '{logicalName}'.");
    }

    private static ZipArchiveEntry FindZipEntry(ZipArchive archive, string fullName)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, fullName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        throw new InvalidDataException(
            $"AEMPS spreadsheet is missing required part '{fullName}'.");
    }

    // Reads xl/sharedStrings.xml (if present) into an indexed list.
    // Some XLSX writers (openpyxl among them) emit values inline via
    // t="inlineStr" and omit sharedStrings.xml entirely — in that case
    // the returned list is empty and every cell in the sheet is read
    // from its own <is><t>.
    private static async Task<List<string>> ReadSharedStringsAsync(
        ZipArchive archive, CancellationToken cancellationToken)
    {
        var list = new List<string>();
        ZipArchiveEntry? entry = null;
        foreach (var candidate in archive.Entries)
        {
            if (string.Equals(candidate.FullName, SharedStringsEntry, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                break;
            }
        }
        if (entry is null)
        {
            return list;
        }

        await using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });

        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "si" && xr.NamespaceURI == SpreadsheetNs)
            {
                list.Add(await ReadSharedStringItemAsync(xr, cancellationToken));
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return list;
    }

    // A <si> can contain either a single <t> (plain string) or a
    // sequence of <r><t>…</t></r> (rich text runs). We concatenate the
    // text of every <t> descendant, dropping formatting.
    private static async Task<string> ReadSharedStringItemAsync(
        XmlReader xr, CancellationToken cancellationToken)
    {
        if (xr.IsEmptyElement)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        var startDepth = xr.Depth;
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.EndElement && xr.Depth == startDepth)
            {
                break;
            }
            if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "t" && xr.NamespaceURI == SpreadsheetNs)
            {
                if (xr.IsEmptyElement)
                {
                    continue;
                }
                sb.Append(await xr.ReadElementContentAsStringAsync());
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return sb.ToString();
    }

    private static async IAsyncEnumerable<ReferenceMedicineRow> ReadRowsAsync(
        ZipArchiveEntry sheet,
        List<string> sharedStrings,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = sheet.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });

        // Reusable buffer sized to the fixed column count. Cleared per
        // row so a short row's trailing cells default to empty string.
        var cells = new string[Col_Count];
        var isHeader = true;

        while (await xr.ReadAsync())
        {
            if (xr.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (xr.LocalName != "row" || xr.NamespaceURI != SpreadsheetNs)
            {
                continue;
            }

            Array.Clear(cells);
            await ReadRowCellsAsync(xr, sharedStrings, cells, cancellationToken);

            if (isHeader)
            {
                isHeader = false;
                ValidateHeader(cells);
                continue;
            }

            var row = MapRow(cells);
            if (row is null)
            {
                report.RecordSkip();
                continue;
            }
            yield return row;
        }
    }

    private static async Task ReadRowCellsAsync(
        XmlReader xr,
        List<string> sharedStrings,
        string[] cells,
        CancellationToken cancellationToken)
    {
        if (xr.IsEmptyElement)
        {
            return;
        }

        var startDepth = xr.Depth;
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.EndElement && xr.Depth == startDepth)
            {
                return;
            }
            if (xr.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (xr.LocalName != "c" || xr.NamespaceURI != SpreadsheetNs)
            {
                continue;
            }

            var reference = xr.GetAttribute("r"); // e.g. "A5"
            var type = xr.GetAttribute("t");      // e.g. "s", "inlineStr", "str", "b", null
            var columnIndex = ColumnIndexFromReference(reference);
            var value = xr.IsEmptyElement
                ? string.Empty
                : await ReadCellValueAsync(xr, type, sharedStrings);

            if (columnIndex >= 0 && columnIndex < cells.Length)
            {
                cells[columnIndex] = value;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static async Task<string> ReadCellValueAsync(
        XmlReader xr, string? type, List<string> sharedStrings)
    {
        var startDepth = xr.Depth;
        string? vText = null;
        var sb = new System.Text.StringBuilder(); // for inline strings

        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.EndElement && xr.Depth == startDepth)
            {
                break;
            }
            if (xr.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (xr.NamespaceURI != SpreadsheetNs)
            {
                continue;
            }

            switch (xr.LocalName)
            {
                case "v":
                    if (!xr.IsEmptyElement)
                    {
                        vText = await xr.ReadElementContentAsStringAsync();
                    }
                    break;
                case "is":
                    // Inline string: <is><t>value</t></is> or <is><r><t>…</t></r>…</is>
                    if (!xr.IsEmptyElement)
                    {
                        await AppendInlineTextsAsync(xr, sb);
                    }
                    break;
                default:
                    break;
            }
        }

        if (type == "s")
        {
            if (vText is null || !int.TryParse(vText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var idx))
            {
                return string.Empty;
            }
            return idx >= 0 && idx < sharedStrings.Count ? sharedStrings[idx] : string.Empty;
        }
        if (type == "inlineStr")
        {
            return sb.ToString();
        }
        // "str" (formula string), "b", numeric default, "d" (date):
        // return the raw <v> text. We only actually consume string
        // columns in this parser; numeric columns end up unread.
        return vText ?? string.Empty;
    }

    private static async Task AppendInlineTextsAsync(XmlReader xr, System.Text.StringBuilder sb)
    {
        var startDepth = xr.Depth;
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.EndElement && xr.Depth == startDepth)
            {
                return;
            }
            if (xr.NodeType == XmlNodeType.Element
                && xr.LocalName == "t"
                && xr.NamespaceURI == SpreadsheetNs
                && !xr.IsEmptyElement)
            {
                sb.Append(await xr.ReadElementContentAsStringAsync());
            }
        }
    }

    // Converts an OpenXML cell reference (e.g. "A", "B", "AA") into a
    // zero-based column index. Any digits (row number) are ignored.
    // Returns -1 for a null or malformed reference.
    private static int ColumnIndexFromReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return -1;
        }
        var col = 0;
        for (var i = 0; i < reference.Length; i++)
        {
            var ch = reference[i];
            if (ch >= 'A' && ch <= 'Z')
            {
                col = col * 26 + (ch - 'A' + 1);
                continue;
            }
            if (ch >= 'a' && ch <= 'z')
            {
                col = col * 26 + (ch - 'a' + 1);
                continue;
            }
            if (ch >= '0' && ch <= '9')
            {
                break; // rest is the row number
            }
            return -1;
        }
        return col - 1;
    }

    private static void ValidateHeader(string[] cells)
    {
        Expect(cells, Col_NRegistro, "Nº Registro");
        Expect(cells, Col_Medicamento, "Medicamento");
        Expect(cells, Col_Laboratorio, "Laboratorio");
        Expect(cells, Col_Estado, "Estado");
        Expect(cells, Col_Atc, "Cód. ATC");
        Expect(cells, Col_PrincipiosActivos, "Principios Activos");
        Expect(cells, Col_Observaciones, "Observaciones");
    }

    private static void Expect(string[] cells, int index, string expected)
    {
        var actual = cells[index] ?? string.Empty;
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"AEMPS spreadsheet header mismatch at column {index}: expected '{expected}', got '{actual}'.");
        }
    }

    private static ReferenceMedicineRow? MapRow(string[] cells)
    {
        var nregistro = cells[Col_NRegistro] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nregistro))
        {
            return null;
        }

        var commercialName = cells[Col_Medicamento] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(commercialName))
        {
            return null;
        }

        var ingredients = BuildIngredients(cells[Col_PrincipiosActivos], cells[Col_Atc]);

        return new ReferenceMedicineRow(
            Country: Spain,
            NationalCode: nregistro,
            CommercialName: commercialName,
            PharmaceuticalForm: null,
            Dosage: null,
            MarketingAuthorisationHolder: NullIfBlank(cells[Col_Laboratorio]),
            MarketingStatus: NullIfBlank(cells[Col_Estado]),
            DispensingRegime: NullIfBlank(cells[Col_Observaciones]),
            LinkLeaflet: null,
            LinkSpc: null,
            ActiveIngredients: ingredients);
    }

    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(
        string? rawActive, string? rawAtc)
    {
        if (string.IsNullOrWhiteSpace(rawActive))
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var atc = AtcCode.TryParse(rawAtc ?? string.Empty, out var parsed) ? parsed : (AtcCode?)null;

        var parts = rawActive.Split(ActiveIngredientSeparator, StringSplitOptions.RemoveEmptyEntries);
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

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
