using System.IO.Compression;

namespace MedReminder.Infrastructure.Tests.Catalogue;

// Repackages the M0 fixture CSVs into the ZIP layout the AIFA
// parser expects. Tests call BuildAifaSnapshotStream() to get a
// fresh Stream per test — the parser never mutates the archive, but
// keeping streams disposable keeps the test resource lifecycle
// unambiguous.
internal static class CatalogueFixtures
{
    private const string FixtureDirectory = "fixtures/catalogue";

    public const string ConfezioniCsv = "aifa-confezioni-sample.csv";
    public const string PaCsv = "aifa-pa-sample.csv";

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

    private static void AddEntry(ZipArchive archive, string logicalName, string fixtureName)
    {
        var entry = archive.CreateEntry(logicalName, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        var path = Path.Combine(AppContext.BaseDirectory, FixtureDirectory, fixtureName);
        using var file = File.OpenRead(path);
        file.CopyTo(entryStream);
    }
}
