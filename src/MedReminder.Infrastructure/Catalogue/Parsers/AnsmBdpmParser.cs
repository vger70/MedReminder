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
//   - Encoding:  ISO-8859-15 (a code page — the parser registers the
//     CodePages provider defensively on first use).
//   - Header row: absent. Columns are positional and documented at
//     https://base-donnees-publique.medicaments.gouv.fr/telechargement.php
//     ("Description des fichiers de la BDPM").
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

    private static readonly CountryCode France = CountryCode.Parse("FR");

    // ISO-8859-15 is a code page. .NET on non-Windows targets only
    // registers the ASCII / Latin1 / UTF-* families by default; we
    // register the CodePages provider defensively so the parser works
    // in every host that consumes it (Windows runtime + test host).
    // Encoding.RegisterProvider is idempotent, so repeated calls from
    // parallel imports are a no-op.
    private static readonly Encoding Iso8859_15 = ResolveIso8859_15();

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
        using var reader = new StreamReader(stream, Iso8859_15);

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
            }

            // Deduplicate while preserving source order — a single CIS
            // often has one COMPO row per pharmaceutical element (e.g.
            // gélule + solution buvable) with the same substance
            // repeated verbatim.
            if (!list.Contains(substance, StringComparer.OrdinalIgnoreCase))
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
        using var reader = new StreamReader(stream, Iso8859_15);

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
                MarketingAuthorisationHolder: NullIfBlank(TrimTitulaire(FieldOrEmpty(fields, CisIdx_Titulaire))),
                MarketingStatus: NullIfBlank(fields[CisIdx_StatutAmm]),
                DispensingRegime: null,
                LinkLeaflet: null,
                LinkSpc: null,
                ActiveIngredients: ingredients);
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

    // Upstream CIS_bdpm rows sometimes ship the titulaire field with a
    // leading space (" PHARMA DEVELOPPEMENT"). Trim to keep the DB row
    // consistent with the other parsers.
    private static string TrimTitulaire(string raw) => raw.Trim();

    private static string FieldOrEmpty(string[] fields, int index)
        => index < fields.Length ? fields[index] : string.Empty;

    private static async Task<Stream> BufferAsync(Stream input, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    private static string? NullIfBlank(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Encoding ResolveIso8859_15()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("iso-8859-15");
    }
}
