using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Tests.Support;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// Orchestration of the remote AIFA feed with a fake transport and a
// fake importer: version decision, verification, staging cleanup.
public sealed class RemoteCatalogueRefresherTests : IDisposable
{
    private readonly string _appData = Path.Combine(
        Path.GetTempPath(), "medreminder-remote-feed-" + Guid.NewGuid().ToString("N"));

    private string Staging => RemoteCatalogueRefresher.GetStagingDirectory(_appData);

    public void Dispose()
    {
        if (Directory.Exists(_appData))
        {
            Directory.Delete(_appData, recursive: true);
        }
    }

    [Fact]
    public async Task Imports_a_newer_snapshot_and_deletes_the_staged_file()
    {
        var archive = BuildArchive();
        var feed = new FakeFeed(Manifest("202610", Sha(archive), archive.Length), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));
        var refresher = Build(feed, importer);

        var outcome = await refresher.RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
        importer.Imports.Should().ContainSingle();
        importer.Imports[0].Should().Be(("IT", "202610", 500, archive.Length));
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Fact]
    public async Task Imports_into_an_empty_catalogue_with_a_minimum_of_one_row()
    {
        var archive = BuildArchive();
        var feed = new FakeFeed(Manifest("202610"), archive);
        var importer = new FakeImporter(new CatalogueImportState(null, 0));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
        importer.Imports.Single().MinimumRowCount.Should().Be(1);
    }

    [Theory]
    [InlineData("202610")]
    [InlineData("202611")]
    public async Task Does_not_download_when_the_local_version_is_equal_or_newer(string local)
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState(local, 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(0);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Reports_an_unavailable_manifest()
    {
        var feed = new FakeFeed(manifest: null, BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.ManifestUnavailable);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_hash_mismatch_without_importing_and_cleans_up()
    {
        var archive = BuildArchive();
        var feed = new FakeFeed(Manifest("202610", sha256: new string('0', 64)), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_size_mismatch()
    {
        var archive = BuildArchive();
        var feed = new FakeFeed(Manifest("202610", size: archive.Length + 1), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_archive_without_the_AIFA_entries()
    {
        var archive = BuildArchive(includePa: false);
        var feed = new FakeFeed(Manifest("202610"), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_zip()
    {
        var feed = new FakeFeed(Manifest("202610"), Encoding.ASCII.GetBytes("<html>not a zip</html>"));
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Fact]
    public async Task Reports_an_importer_rejection_and_cleans_up()
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000))
        {
            ImportFailure = new InvalidDataException("too few rows"),
        };

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Fact]
    public async Task Reports_a_download_failure_and_cleans_up_the_partial_file()
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive())
        {
            DownloadFailure = new HttpRequestException("HTTP 500"),
        };
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Failed);
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Fact]
    public async Task Removes_leftovers_of_an_interrupted_run()
    {
        Directory.CreateDirectory(Staging);
        var leftover = Path.Combine(Staging, "aifa-202609.zip.part");
        await File.WriteAllTextAsync(leftover, "partial");
        var feed = new FakeFeed(Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        await Build(feed, importer).RunAsync(CancellationToken.None);

        File.Exists(leftover).Should().BeFalse();
    }

    [Fact]
    public void ValidateArchive_rejects_entries_above_the_uncompressed_cap()
    {
        // ZipArchiveEntry.Length is read from the central directory, so
        // an archive that only declares a huge uncompressed size is what
        // a zip bomb looks like to the check.
        var archive = BuildArchive();
        DeclareUncompressedSize(archive, 0x30000000u);
        using var stream = new MemoryStream(archive);

        var act = () => RemoteCatalogueRefresher.ValidateArchive(stream);

        act.Should().Throw<InvalidDataException>().WithMessage("*limit*");
    }

    [Fact]
    public void ValidateArchive_accepts_entries_in_any_folder_and_any_case()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "aifa/CONFEZIONI_FORNITURA.CSV", "CODICE_AIC\n");
            Write(zip, "aifa/pa_confezioni.csv", "CODICE_AIC\n");
        }
        buffer.Position = 0;

        var act = () => RemoteCatalogueRefresher.ValidateArchive(buffer);

        act.Should().NotThrow();
    }

    // Rewrites the uncompressed-size field (offset 24) of every central
    // directory header (signature PK\x01\x02).
    private static void DeclareUncompressedSize(byte[] archive, uint size)
    {
        for (var i = 0; i + 28 <= archive.Length; i++)
        {
            if (archive[i] == 0x50 && archive[i + 1] == 0x4B && archive[i + 2] == 0x01 && archive[i + 3] == 0x02)
            {
                BitConverter.GetBytes(size).CopyTo(archive, i + 24);
            }
        }
    }

    private RemoteCatalogueRefresher Build(FakeFeed feed, FakeImporter importer) =>
        new(feed, importer, new FixedAppData(_appData), new CapturingLogger<RemoteCatalogueRefresher>());

    private static CatalogueFeedManifest Manifest(string version, string? sha256 = null, long? size = null) =>
        new(version, $"aifa-{version}.zip", null, sha256, size);

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] BuildArchive(bool includePa = true)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "confezioni_fornitura.csv", "CODICE_AIC;DENOMINAZIONE\n012345678;TEST\n");
            if (includePa)
            {
                Write(zip, "PA_confezioni.csv", "CODICE_AIC;PRINCIPIO_ATTIVO\n012345678;TEST\n");
            }
        }
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    private sealed class FixedAppData(string directory) : IAppDataLocation
    {
        public string DataDirectory => directory;
    }

    private sealed class FakeFeed(CatalogueFeedManifest? manifest, byte[] payload) : ICatalogueFeedClient
    {
        public int Downloads { get; private set; }

        public Exception? DownloadFailure { get; init; }

        public Task<CatalogueFeedManifest?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(manifest);

        public async Task<CatalogueFeedDownload> DownloadAsync(
            CatalogueFeedManifest m, string destinationPath, CancellationToken cancellationToken)
        {
            Downloads++;
            var part = destinationPath + ".part";
            await File.WriteAllBytesAsync(part, payload, cancellationToken);
            if (DownloadFailure is not null)
            {
                throw DownloadFailure;
            }
            File.Move(part, destinationPath, overwrite: true);
            return new CatalogueFeedDownload(payload.Length, Sha(payload));
        }
    }

    private sealed class FakeImporter(CatalogueImportState state) : IReferenceCatalogueImporter
    {
        public List<(string Country, string Version, int MinimumRowCount, long Bytes)> Imports { get; } = new();

        public Exception? ImportFailure { get; init; }

        public Task<ImportReport> ImportAsync(
            Stream snapshot, CountryCode expectedCountry, string snapshotVersion, CancellationToken cancellationToken) =>
            ImportAsync(snapshot, expectedCountry, snapshotVersion, 1, cancellationToken);

        public async Task<ImportReport> ImportAsync(
            Stream snapshot, CountryCode expectedCountry, string snapshotVersion,
            int minimumRowCount, CancellationToken cancellationToken)
        {
            if (ImportFailure is not null)
            {
                throw ImportFailure;
            }

            using var copy = new MemoryStream();
            await snapshot.CopyToAsync(copy, cancellationToken);
            Imports.Add((expectedCountry.Value, snapshotVersion, minimumRowCount, copy.Length));
            return new ImportReport(1, 0, 0, 0, snapshotVersion, DateTimeOffset.UnixEpoch);
        }

        public Task<CatalogueImportState> GetImportStateAsync(CountryCode country, CancellationToken cancellationToken) =>
            Task.FromResult(state);
    }
}
