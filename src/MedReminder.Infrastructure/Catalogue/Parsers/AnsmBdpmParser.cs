using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using MedReminder.Domain.Catalogue;

namespace MedReminder.Infrastructure.Catalogue.Parsers;

// Parses an ANSM / BDPM open-data snapshot into ReferenceMedicineRow
// instances for the French catalogue (ANALYSIS-DRUG-CATALOGUE.md
// §3.5 M4).
//
// Input format: a ZIP archive with two TSV entries at the root:
//   - CIS_bdpm.txt        (one row per medicinal product, key = CIS)
//   - CIS_COMPO_bdpm.txt  (one row per medicinal product × active
//                          substance, join key = CIS)
// A third entry, CIS_CIP_bdpm.txt (one row per package, key = CIP), is
// present in the shipped snapshot for completeness but is not consumed
// here: MedReminder's reference catalogue keys on CIS, not CIP, and the
// upstream CIP file ships with a different (UTF-8) encoding than the
// two we do parse. Skipping it also lets a single ISO-8859-15 encoding
// path handle both files we read.
//
// Wire format for the two consumed files:
//   - Delimiter: TAB (`\t`).
//   - Encoding:  Windows-1252 (cp1252). The ANSM portal documents it
//     as ISO-8859-15, but the actual files contain cp1252-only bytes
//     — 0x92 (curly single-quote `’`, used as apostrophe in
//     `d’organes`, `Pack d’initiation`, `CARMIN D’INDIGO`) is present
//     dozens of times in every current export. Reading those bytes as
//     ISO-8859-15 turns them into the U+0092 C1 control character.
//     Windows-1252 is a strict superset of ISO-8859-1 in the 0xA0–0xFF
//     range and additionally maps every byte in 0x80–0x9F to a
//     graphical character, so it round-trips both the mis-documented
//     cp1252 bytes and the standard Latin-1 accented letters. The
//     parser registers the CodePages provider defensively on first
//     use so the code page resolves on every host that runs it.
//   - Header row: absent. Columns are positional and documented at
//     https://base-donnees-publique.medicaments.gouv.fr/telechargement.php
//     ("Description des fichiers de la BDPM"). The parser validates
//     the first non-empty row's Statut administratif AMM starts with
//     "Autorisation" as a shape sanity check — if ANSM ever renumbers
//     the columns, that check trips loudly instead of the mis-mapped
//     fields silently corrupting the DB.
//
// Import filters applied here:
//   - Skip rows whose CIS_bdpm[5] (Type de procédure AMM) starts with
//     "Enreg homéo" — homeopathic authorisations carry no meaningful
//     active ingredient for the autocomplete, same rationale as the
//     Omeopatico filter in AifaSnapshotParser (§3.2). Recorded as
//     Skipped in ParseReport.
//   - Every emitted row carries `country = 'FR'`.
//   - ATC is NOT present in BDPM base as a structured column, so
//     ActiveIngredientRow.Atc is always null — same convention as
//     EmaEparParser for the sub-set of EPAR rows that lack an ATC.
//   - PharmaceuticalForm ← CIS_bdpm[2]; Dosage ← CIS_bdpm[3] (Voies
//     d'administration, `;`-separated); MarketingStatus ← CIS_bdpm[4];
//     MarketingAuthorisationHolder ← CIS_bdpm[10] (leading whitespace
//     trimmed — upstream ships titulaires with a spurious leading
//     space); DispensingRegime, LinkLeaflet, LinkSpc all null (BDPM
//     base does not expose them as structured columns).
//
// Human-only dataset: BDPM covers human medicinal products; veterinary
// products live in the separate ANMV Bdmap-Vet register and never
// appear here, so no explicit species filter is needed.
internal sealed class AnsmBdpmParser : IReferenceSnapshotParser
{
    private const string CisEntryName = "CIS_bdpm.txt";
    private const string CompoEntryName = "CIS_COMPO_bdpm.txt";
    private const string HomeopathicProcedurePrefix = "Enreg homéo";

    private const int CisIdx_Cis = 0;
    private const int CisIdx_Denomination = 1;
    private const int CisIdx_Form = 2;
    private const int CisIdx_Voies = 3;
    private const int CisIdx_StatutAmm = 4;
    private const int CisIdx_TypeProcedure = 5;
    private const int CisIdx_Titulaire = 10;
    private const int CisIdx_MinColumns = 11;

    private const int CompoIdx_Cis = 0;
    private const int CompoIdx_SubstanceName = 3;
    private const int CompoIdx_MinColumns = 4;

    private const string ExpectedStatutPrefix = "Autorisation";

    private static readonly CountryCode France = CountryCode.Parse("FR");

    // Windows-1252 is a code page. .NET on non-Windows targets only
    // registers the ASCII / Latin1 / UTF-* families by default; we
    // register the CodePages provider defensively so the parser works
    // in every host that consumes it (Windows runtime + test host).
    // Encoding.RegisterProvider is idempotent, so repeated calls from
    // parallel imports are a no-op.
    private static readonly Encoding Windows1252 = ResolveWindows1252();

    public IReadOnlyCollection<CountryCode> SupportedCountries { get; } = new[] { France };

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
            var compoEntry = FindEntry(archive, CompoEntryName);
            var cisEntry = FindEntry(archive, CisEntryName);

            var substancesByCis = await ReadCompositionsAsync(compoEntry, cancellationToken);

            await foreach (var row in ReadMedicinesAsync(cisEntry, substancesByCis, report, cancellationToken))
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
            $"BDPM snapshot is missing required entry '{logicalName}'.");
    }

    private static async Task<Dictionary<string, List<string>>> ReadCompositionsAsync(
        ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var byCis = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Windows1252);

        // Parallel HashSet per CIS keeps dedup at O(1) per substance
        // insert. A pathological polytherapy CIS (~10 substances
        // repeated across dozens of pharmaceutical-element rows)
        // otherwise costs O(n²) on the linear List.Contains scan.
        var seenPerCis = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < CompoIdx_MinColumns)
            {
                continue;
            }

            var cis = fields[CompoIdx_Cis];
            var substance = fields[CompoIdx_SubstanceName].Trim();
            if (string.IsNullOrEmpty(cis) || string.IsNullOrEmpty(substance))
            {
                continue;
            }

            if (!byCis.TryGetValue(cis, out var list))
            {
                list = new List<string>(capacity: 2);
                byCis[cis] = list;
                seenPerCis[cis] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Deduplicate while preserving source order — a single CIS
            // often has one COMPO row per pharmaceutical element
            // (e.g. gélule + solution buvable) with the same substance
            // repeated verbatim.
            if (seenPerCis[cis].Add(substance))
            {
                list.Add(substance);
            }
        }

        return byCis;
    }

    private static async IAsyncEnumerable<ReferenceMedicineRow> ReadMedicinesAsync(
        ZipArchiveEntry entry,
        Dictionary<string, List<string>> substancesByCis,
        ParseReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Windows1252);

        var shapeValidated = false;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < CisIdx_MinColumns)
            {
                report.RecordSkip();
                continue;
            }

            if (!shapeValidated)
            {
                ValidateShape(fields);
                shapeValidated = true;
            }

            if (fields[CisIdx_TypeProcedure].StartsWith(HomeopathicProcedurePrefix, StringComparison.Ordinal))
            {
                report.RecordSkip();
                continue;
            }

            var cis = fields[CisIdx_Cis];
            if (string.IsNullOrWhiteSpace(cis))
            {
                report.RecordSkip();
                continue;
            }

            var commercialName = fields[CisIdx_Denomination];
            if (string.IsNullOrWhiteSpace(commercialName))
            {
                report.RecordSkip();
                continue;
            }

            substancesByCis.TryGetValue(cis, out var substances);
            var ingredients = BuildIngredients(substances);

            yield return new ReferenceMedicineRow(
                Country: France,
                NationalCode: cis,
                CommercialName: commercialName,
                PharmaceuticalForm: NullIfBlank(fields[CisIdx_Form]),
                Dosage: NullIfBlank(fields[CisIdx_Voies]),
                MarketingAuthorisationHolder: NullIfBlank(fields[CisIdx_Titulaire].Trim()),
                MarketingStatus: NullIfBlank(fields[CisIdx_StatutAmm]),
                DispensingRegime: null,
                LinkLeaflet: null,
                LinkSpc: null,
                ActiveIngredients: ingredients);
        }
    }

    // Shape sanity check on the first non-short row. CIS_bdpm has no
    // header — columns are positional, documented at the BDPM portal.
    // If ANSM ever inserts or shifts a column, every downstream
    // MarketingAuthorisationHolder / MarketingStatus / TypeProcedure
    // read would map to the wrong field and the parser would silently
    // corrupt every FR row. Instead: assert that the Statut column
    // starts with "Autorisation" (the invariant prefix of every value
    // BDPM writes there — "Autorisation active", "Autorisation
    // retirée", "Autorisation abrogée", "Autorisation archivée"). A
    // schema change trips this check loudly.
    private static void ValidateShape(string[] fields)
    {
        var statut = fields[CisIdx_StatutAmm];
        if (!statut.StartsWith(ExpectedStatutPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"BDPM CIS_bdpm.txt Statut administratif AMM column (index {CisIdx_StatutAmm}) " +
                $"does not start with '{ExpectedStatutPrefix}' as expected — got '{statut}'. " +
                "ANSM may have changed the column order; verify the layout at " +
                "https://base-donnees-publique.medicaments.gouv.fr/telechargement.php " +
                "and adjust AnsmBdpmParser.CisIdx_* accordingly.");
        }
    }

    private static IReadOnlyList<ReferenceActiveIngredientRow> BuildIngredients(List<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return Array.Empty<ReferenceActiveIngredientRow>();
        }

        var rows = new ReferenceActiveIngredientRow[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            rows[i] = new ReferenceActiveIngredientRow(names[i], Atc: null);
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

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Encoding ResolveWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("windows-1252");
    }
}
