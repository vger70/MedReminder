using System.IO.Compression;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Repackages the fixture CSVs into the ZIP layout the parsers
// expect. Tests call one of the Build*SnapshotStream() helpers to
// get a fresh Stream per test — the parsers never mutate the
// archive, but keeping streams disposable keeps the test resource
// lifecycle unambiguous.
internal static class CatalogueFixtures
{
    private const string FixtureDirectory = "fixtures/catalogue";

    public const string ConfezioniCsv = "aifa-confezioni-sample.csv";
    public const string PaCsv = "aifa-pa-sample.csv";
    public const string EmaEparCsv = "ema-epar-sample.csv";
    public const string AempsXlsx = "aemps-cima-sample.xlsx";
    public const string BdpmCisTxt = "bdpm-cis-sample.txt";
    public const string BdpmCompoTxt = "bdpm-compo-sample.txt";
    public const string BdpmCipTxt = "bdpm-cip-sample.txt";

    public static Stream BuildAifaSnapshotStream()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "confezioni_fornitura.csv", ConfezioniCsv);
            AddEntry(archive, "PA_confezioni.csv", PaCsv);
        }
        buffer.Position = 0;
        return buffer;
    }

    public static Stream BuildEmaEparSnapshotStream()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "ema-epar.csv", EmaEparCsv);
        }
        buffer.Position = 0;
        return buffer;
    }

    // AEMPS snapshot shape: outer ZIP with a single `aemps.xlsx` entry
    // at the root, mirroring what ships in Assets/Catalogue/es/. The
    // fixture xlsx is a stratified subset of the real Medicamentos.xls
    // export; the parser test verifies it round-trips.
    public static Stream BuildAempsSnapshotStream()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "aemps.xlsx", AempsXlsx);
        }
        buffer.Position = 0;
        return buffer;
    }

    // BDPM snapshot shape: outer ZIP with three TSV entries at the
    // root (CIS + COMPO + CIP), mirroring what ANSM publishes and what
    // ships in Assets/Catalogue/fr/. The parser only reads CIS and
    // COMPO; CIP is included so the fixture matches the on-disk shape.
    public static Stream BuildBdpmSnapshotStream()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "CIS_bdpm.txt", BdpmCisTxt);
            AddEntry(archive, "CIS_COMPO_bdpm.txt", BdpmCompoTxt);
            AddEntry(archive, "CIS_CIP_bdpm.txt", BdpmCipTxt);
        }
        buffer.Position = 0;
        return buffer;
    }

    private static void AddEntry(ZipArchive archive, string logicalName, string fixtureName)
    {
        var entry = archive.CreateEntry(logicalName, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        var path = Path.Combine(AppContext.BaseDirectory, FixtureDirectory, fixtureName);
        using var file = File.OpenRead(path);
        file.CopyTo(entryStream);
    }
}
