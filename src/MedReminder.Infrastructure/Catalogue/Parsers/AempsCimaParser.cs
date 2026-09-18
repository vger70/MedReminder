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
    private const string PackageRelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeDocumentRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string WorksheetRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private const string SharedStringsEntry = "xl/sharedStrings.xml";
    private const string ActiveIngredientSeparator = ", ";

    private const int Col_NRegistro = 0;
    private const int Col_Medicamento = 1;
    private const int Col_Laboratorio = 2;
    private const int Col_Estado = 4;
    private const int Col_Atc = 6;
    private const int Col_PrincipiosActivos = 7;
    private const int Col_NPActivos = 8;
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
            // seekable, so buffer it once. The DeflateStream backing
            // the ZipArchive entry is disposed here — leaving the
            // buffer copy as the seekable snapshot the inner reader
            // consumes.
            await using var xlsxEntryStream = xlsxEntry.Open();
            await using var xlsxBuffer = await BufferAsync(xlsxEntryStream, cancellationToken);
            using var xlsx = new ZipArchive(xlsxBuffer, ZipArchiveMode.Read, leaveOpen: true);

            var sharedStrings = await ReadSharedStringsAsync(xlsx, cancellationToken);
            var sheet = await ResolveFirstSheetEntryAsync(xlsx, cancellationToken);

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

    // Walk the OOXML package relationships to find the *first sheet's*
    // actual worksheet part path, instead of assuming it lives at
    // xl/worksheets/sheet1.xml. Different XLSX writers name it
    // differently (Excel keeps sheet1.xml, Libre Office rewrites on
    // save, some tools use a workbook-relative path). The resolution
    // chain: package `_rels/.rels` → workbook part path (typically
    // xl/workbook.xml) → workbook `_rels/workbook.xml.rels` →
    // r:id of the first `<sheet>` in `<sheets>` → target path.
    // Falls back to the first `xl/worksheets/*.xml` entry when the
    // relationships cannot be traversed (defensive; AEMPS's own
    // export follows the standard, but leave the fallback so a
    // malformed package still parses instead of throwing).
    private static async Task<ZipArchiveEntry> ResolveFirstSheetEntryAsync(
        ZipArchive xlsx, CancellationToken cancellationToken)
    {
        string? workbookPath = await FindTargetOfTypeAsync(
            xlsx, "_rels/.rels", OfficeDocumentRelType, cancellationToken);
        if (workbookPath is not null)
        {
            workbookPath = NormalizeRelativePath(workbookPath, baseDir: string.Empty);
            var workbookRelsPath = SiblingRelsPath(workbookPath);
            var firstSheetRid = await ReadFirstSheetRidAsync(xlsx, workbookPath, cancellationToken);
            if (firstSheetRid is not null)
            {
                var sheetTarget = await FindTargetOfIdAsync(
                    xlsx, workbookRelsPath, firstSheetRid, cancellationToken);
                if (sheetTarget is not null)
                {
                    var workbookDir = PathDirectory(workbookPath);
                    var absolute = NormalizeRelativePath(sheetTarget, workbookDir);
                    var entry = TryFindZipEntry(xlsx, absolute);
                    if (entry is not null)
                    {
                        return entry;
                    }
                }
            }
        }

        // Fallback: pick the alphabetically-first worksheet part.
        foreach (var candidate in xlsx.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (candidate.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                && candidate.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        throw new InvalidDataException(
            "AEMPS spreadsheet has no worksheet part under xl/worksheets/.");
    }

    private static async Task<string?> ReadFirstSheetRidAsync(
        ZipArchive xlsx, string workbookPath, CancellationToken cancellationToken)
    {
        var entry = TryFindZipEntry(xlsx, workbookPath);
        if (entry is null)
        {
            return null;
        }
        await using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.Element
                && xr.LocalName == "sheet"
                && xr.NamespaceURI == SpreadsheetNs)
            {
                // r:id lives in the "…/relationships" namespace but
                // XmlReader.GetAttribute("id", ns) matches by prefix
                // any way; use the literal "r:id" as sold by every
                // real workbook.
                var rid = xr.GetAttribute("r:id")
                    ?? xr.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                return rid;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return null;
    }

    private static async Task<string?> FindTargetOfTypeAsync(
        ZipArchive xlsx, string relsPath, string type, CancellationToken cancellationToken)
    {
        var entry = TryFindZipEntry(xlsx, relsPath);
        if (entry is null)
        {
            return null;
        }
        await using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.Element
                && xr.LocalName == "Relationship"
                && xr.NamespaceURI == PackageRelsNs
                && string.Equals(xr.GetAttribute("Type"), type, StringComparison.Ordinal))
            {
                return xr.GetAttribute("Target");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return null;
    }

    private static async Task<string?> FindTargetOfIdAsync(
        ZipArchive xlsx, string relsPath, string rid, CancellationToken cancellationToken)
    {
        var entry = TryFindZipEntry(xlsx, relsPath);
        if (entry is null)
        {
            return null;
        }
        await using var stream = entry.Open();
        using var xr = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        });
        while (await xr.ReadAsync())
        {
            if (xr.NodeType == XmlNodeType.Element
                && xr.LocalName == "Relationship"
                && xr.NamespaceURI == PackageRelsNs
                && string.Equals(xr.GetAttribute("Id"), rid, StringComparison.Ordinal)
                && string.Equals(xr.GetAttribute("Type"), WorksheetRelType, StringComparison.Ordinal))
            {
                return xr.GetAttribute("Target");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        return null;
    }

    private static string SiblingRelsPath(string partPath)
    {
        var dir = PathDirectory(partPath);
        var file = partPath.Substring(dir.Length == 0 ? 0 : dir.Length + 1);
        return dir.Length == 0 ? $"_rels/{file}.rels" : $"{dir}/_rels/{file}.rels";
    }

    private static string PathDirectory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path.Substring(0, slash);
    }

    private static string NormalizeRelativePath(string target, string baseDir)
    {
        if (target.Length > 0 && target[0] == '/')
        {
            return target.TrimStart('/');
        }
        return baseDir.Length == 0 ? target : $"{baseDir}/{target}";
    }

    private static ZipArchiveEntry? TryFindZipEntry(ZipArchive archive, string fullName)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, fullName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
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
    //
    // Uses ReadSubtree so the reader can never overshoot into the
    // next <si> sibling. `ReadElementContentAsStringAsync` advances
    // past the end tag it consumed; without the subtree isolation
    // that would leave the main reader on the sibling element and
    // any depth-based loop bound would silently keep processing it.
    private static async Task<string> ReadSharedStringItemAsync(
        XmlReader xr, CancellationToken cancellationToken)
    {
        if (xr.IsEmptyElement)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        using var sub = xr.ReadSubtree();
        await sub.ReadAsync(); // move onto <si> inside the subtree
        while (await sub.ReadAsync())
        {
            if (sub.NodeType == XmlNodeType.Element
                && sub.LocalName == "t"
                && sub.NamespaceURI == SpreadsheetNs
                && !sub.IsEmptyElement)
            {
                sb.Append(await sub.ReadElementContentAsStringAsync());
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

        // Reusable buffer sized to the fixed column count. Filled with
        // empty strings per row so a short row's trailing cells (or
        // absent <c/> gaps between existing cells) read as "" instead
        // of null — every consumer downstream uses `?? string.Empty`
        // as a safety belt too, but starting from "" avoids a
        // NullReferenceException the first time somebody adds a
        // direct cell read.
        var cells = new string[Col_Count];
        var seenHeader = false;

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

            Array.Fill(cells, string.Empty);
            await ReadRowCellsAsync(xr, sharedStrings, cells, cancellationToken);

            if (!seenHeader)
            {
                // Some XLSX writers emit leading empty rows (frozen
                // panes, styling filler, a merged title row above the
                // real header). Skip anything blank until we find the
                // header — then latch and validate it.
                if (IsBlankRow(cells))
                {
                    continue;
                }
                seenHeader = true;
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

    private static bool IsBlankRow(string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (!string.IsNullOrEmpty(cells[i]))
            {
                return false;
            }
        }
        return true;
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

        // ReadSubtree gives us a reader that cannot escape the <row>.
        // The main reader is guaranteed to land past </row> when this
        // helper returns, regardless of what any nested Read* call
        // consumes internally.
        using var rowReader = xr.ReadSubtree();
        await rowReader.ReadAsync(); // move onto <row>
        while (await rowReader.ReadAsync())
        {
            if (rowReader.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (rowReader.LocalName != "c" || rowReader.NamespaceURI != SpreadsheetNs)
            {
                continue;
            }

            var reference = rowReader.GetAttribute("r"); // e.g. "A5"
            var type = rowReader.GetAttribute("t");      // e.g. "s", "inlineStr", "str", "b", null
            var columnIndex = ColumnIndexFromReference(reference);
            var value = rowReader.IsEmptyElement
                ? string.Empty
                : await ReadCellValueAsync(rowReader, type, sharedStrings, cancellationToken);

            if (columnIndex >= 0 && columnIndex < cells.Length)
            {
                cells[columnIndex] = value;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    // Reads one <c> cell. Uses ReadSubtree so the reader stays bounded
    // by </c> — otherwise ReadElementContentAsStringAsync on <v> or
    // <t> would advance the main reader past the end tag onto the
    // next sibling <c>, silently swallowing the rest of the row.
    private static async Task<string> ReadCellValueAsync(
        XmlReader xr, string? type, List<string> sharedStrings, CancellationToken cancellationToken)
    {
        string? vText = null;
        var sb = new System.Text.StringBuilder(); // for inline strings

        using var cellReader = xr.ReadSubtree();
        await cellReader.ReadAsync(); // move onto <c>
        while (await cellReader.ReadAsync())
        {
            if (cellReader.NodeType != XmlNodeType.Element)
            {
                continue;
            }
            if (cellReader.NamespaceURI != SpreadsheetNs)
            {
                continue;
            }

            switch (cellReader.LocalName)
            {
                case "v":
                    if (!cellReader.IsEmptyElement)
                    {
                        vText = await cellReader.ReadElementContentAsStringAsync();
                    }
                    break;
                case "is":
                    // Inline string: <is><t>value</t></is> or <is><r><t>…</t></r>…</is>
                    if (!cellReader.IsEmptyElement)
                    {
                        await AppendInlineTextsAsync(cellReader, sb, cancellationToken);
                    }
                    break;
                default:
                    break;
            }
            cancellationToken.ThrowIfCancellationRequested();
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

    private static async Task AppendInlineTextsAsync(
        XmlReader xr, System.Text.StringBuilder sb, CancellationToken cancellationToken)
    {
        using var isReader = xr.ReadSubtree();
        await isReader.ReadAsync(); // move onto <is>
        while (await isReader.ReadAsync())
        {
            if (isReader.NodeType == XmlNodeType.Element
                && isReader.LocalName == "t"
                && isReader.NamespaceURI == SpreadsheetNs
                && !isReader.IsEmptyElement)
            {
                sb.Append(await isReader.ReadElementContentAsStringAsync());
            }
            cancellationToken.ThrowIfCancellationRequested();
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

        var ingredients = BuildIngredients(
            cells[Col_PrincipiosActivos],
            cells[Col_Atc],
            cells[Col_NPActivos]);

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

    // Splits Principios Activos into individual substances, guided by
    // the Nº P. Activos column when it is available. AEMPS ships a
    // handful of substance names that themselves embed a ", " —
    // salt/hydrate qualifiers ("REZAFUNGINA, ACETATO DE";
    // "NEVIRAPINA, ANHIDRA"; "CACAHUETE POLVO DESENGRASADO,
    // SEMILLAS"), organometallic bracketed formulas
    // ("[TETRAKIS…], TETRAFLUOROBORATO DE") and more. A blind split on
    // ", " would over-split those rows into wrong ingredients, so we
    // only trust the split when its count matches Nº P. Activos.
    // If the header column is missing or the count doesn't match, we
    // treat the whole cell as a single substance (accepts one loss of
    // granularity, avoids inventing wrong names).
    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(
        string? rawActive, string? rawAtc, string? rawExpectedCount)
    {
        if (string.IsNullOrWhiteSpace(rawActive))
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var atc = AtcCode.TryParse(rawAtc ?? string.Empty, out var parsed) ? parsed : (AtcCode?)null;

        int? expected = null;
        if (!string.IsNullOrWhiteSpace(rawExpectedCount)
            && int.TryParse(rawExpectedCount, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n > 0)
        {
            expected = n;
        }

        if (expected is 1)
        {
            // Ground truth from AEMPS: exactly one substance, even if
            // the value contains ", ". Don't split.
            return new[] { new ReferenceActiveIngredientRow(rawActive.Trim(), atc) };
        }

        var parts = rawActive.Split(ActiveIngredientSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (expected is int e && parts.Length != e)
        {
            // Split disagrees with the authoritative substance count —
            // the ", " separator hit an intra-name comma. Fall back to
            // the whole string as one ingredient (imprecise but never
            // wrong).
            return new[] { new ReferenceActiveIngredientRow(rawActive.Trim(), atc) };
        }

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
