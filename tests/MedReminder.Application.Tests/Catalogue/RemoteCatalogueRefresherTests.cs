using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// Orchestration of a remote catalogue feed with a fake transport and a
// fake importer: version decision, verification, staging cleanup. The
// AIFA cases run on the Italian descriptor; the per-descriptor cases
// check that country, entries and caps follow the feed.
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

        var outcome = await refresher.RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

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

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
        importer.Imports.Single().MinimumRowCount.Should().Be(1);
    }

    [Fact]
    public async Task Imports_an_archive_republished_in_the_same_month_under_a_suffixed_label()
    {
        var archive = BuildArchive();
        var generated = new DateTimeOffset(2026, 10, 5, 3, 0, 12, TimeSpan.Zero);
        var manifest = Manifest("202610", Sha(archive)) with { Generated = generated };
        var feed = new FakeFeed(manifest, archive);
        var importer = new FakeImporter(new CatalogueImportState("202610+20261002T030000Z", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
        importer.Imports.Single().Version.Should().Be("202610+20261005T030012Z");
    }

    [Fact]
    public async Task A_first_import_of_the_month_replaces_the_embedded_snapshot_of_that_month()
    {
        var archive = BuildArchive();
        var manifest = Manifest("202610", Sha(archive)) with { Generated = DateTimeOffset.UnixEpoch.AddYears(56) };
        var feed = new FakeFeed(manifest, archive);
        var importer = new FakeImporter(new CatalogueImportState("202610", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
    }

    [Fact]
    public async Task Does_not_download_when_the_stored_build_is_the_same_or_later()
    {
        var archive = BuildArchive();
        var manifest = Manifest("202610", Sha(archive)) with
        {
            Generated = new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero),
        };
        var feed = new FakeFeed(manifest, archive);
        var importer = new FakeImporter(new CatalogueImportState("202610+20261002T030000Z", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(0);
    }

    [Fact]
    public void LabelFor_uses_the_build_time_only_with_a_hash()
    {
        var generated = new DateTimeOffset(2026, 10, 5, 3, 0, 12, TimeSpan.Zero);
        var hash = new string('a', 64);

        RemoteCatalogueRefresher.LabelFor(Manifest("202610", hash) with { Generated = generated })
            .Should().Be("202610+20261005T030012Z");
        RemoteCatalogueRefresher.LabelFor(Manifest("202610") with { Generated = generated })
            .Should().Be("202610");
        RemoteCatalogueRefresher.LabelFor(Manifest("202610", hash))
            .Should().Be("202610");
    }

    [Theory]
    [InlineData("202610")]
    [InlineData("202611")]
    public async Task Does_not_download_when_the_local_version_is_equal_or_newer(string local)
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState(local, 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(0);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Imports_a_manifest_published_for_Italy()
    {
        var feed = new FakeFeed(Manifest("202610") with { Country = "IT" }, BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
    }

    [Fact]
    public async Task Reports_an_unavailable_manifest()
    {
        var feed = new FakeFeed(manifest: null, BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.ManifestUnavailable);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_hash_mismatch_without_importing_and_cleans_up()
    {
        var archive = BuildArchive();
        var feed = new FakeFeed(Manifest("202610", sha256: new string('0', 64)), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

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

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_archive_without_the_AIFA_entries()
    {
        var archive = BuildArchive(includePa: false);
        var feed = new FakeFeed(Manifest("202610"), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_a_file_that_is_not_a_zip()
    {
        var feed = new FakeFeed(Manifest("202610"), Encoding.ASCII.GetBytes("<html>not a zip</html>"));
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

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

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

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

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Failed);
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("up to date")]
    [InlineData("newer")]
    public async Task Removes_leftovers_of_an_interrupted_run_on_every_start(string situation)
    {
        Directory.CreateDirectory(Staging);
        var leftovers = new[]
        {
            Path.Combine(Staging, "aifa-202609.zip.part"),
            Path.Combine(Staging, "aifa-202609.zip"),
        };
        foreach (var leftover in leftovers)
        {
            await File.WriteAllTextAsync(leftover, "partial");
        }

        var feed = new FakeFeed(situation == "offline" ? null : Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState(situation == "up to date" ? "202610" : "202609", 1000));

        await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        leftovers.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task A_feed_that_throws_while_reading_the_manifest_is_reported_as_unavailable()
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive())
        {
            ManifestFailure = new IOException("connection reset"),
        };
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.ManifestUnavailable);
        importer.Imports.Should().BeEmpty();
    }

    [Fact]
    public async Task The_import_holds_WriteGate_so_use_cases_wait_for_it()
    {
        var feed = new FakeFeed(Manifest("202610"), BuildArchive());
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000)) { BlockImport = true };
        var refresh = Build(feed, importer).RunAsync(CatalogueFeedDescriptor.Italy, CancellationToken.None);
        await importer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var scope = new ApplicationTestScope();
        Task action;
        Task early;
        try
        {
            action = scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
                "Enalapril", "compresse", 1m, 1, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows), default);
            early = await Task.WhenAny(action, Task.Delay(200));
        }
        finally
        {
            // The gate is process-wide: always release it.
            importer.Release.TrySetResult();
        }

        early.Should().NotBeSameAs(action, "a use case must not commit while the catalogue import runs");
        await action.WaitAsync(TimeSpan.FromSeconds(10));
        (await refresh).Should().Be(RemoteCatalogueRefreshOutcome.Imported);
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

        var act = () => RemoteCatalogueRefresher.ValidateArchive(stream, CatalogueFeedDescriptor.Italy);

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

        var act = () => RemoteCatalogueRefresher.ValidateArchive(buffer, CatalogueFeedDescriptor.Italy);

        act.Should().NotThrow();
    }

    public static TheoryData<string, string[]> FeedArchives() => new()
    {
        { "EU", ["ema-epar.csv"] },
        { "ES", ["aemps.xlsx"] },
        { "FR", ["CIS_bdpm.txt", "CIS_COMPO_bdpm.txt", "CIS_CIP_bdpm.txt"] },
    };

    [Theory]
    [MemberData(nameof(FeedArchives))]
    public async Task Imports_each_feed_into_its_own_country_under_its_own_archive_name(string country, string[] entries)
    {
        var feed = Descriptor(country);
        var archive = BuildArchive(entries);
        var client = new FakeFeed(Manifest(feed, "202610", Sha(archive), archive.Length), archive);
        var importer = new FakeImporter(new CatalogueImportState("202609", 3000));

        var outcome = await Build(client, importer).RunAsync(feed, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Imported);
        client.Requests.Should().Equal(country);
        client.DownloadPaths.Should().Equal($"{feed.Prefix}-202610.zip");
        importer.StateRequests.Should().Equal(country);
        importer.Imports.Should().Equal((country, "202610", 1500, archive.Length));
        Directory.EnumerateFiles(Staging).Should().BeEmpty();
    }

    [Theory]
    [InlineData("EU", "aemps.xlsx")]
    [InlineData("ES", "ema-epar.csv")]
    [InlineData("FR", "CIS_bdpm.txt")]
    [InlineData("IT", "confezioni_fornitura.csv")]
    public async Task Rejects_an_archive_missing_an_entry_the_feed_requires(string country, string onlyEntry)
    {
        var feed = Descriptor(country);
        var client = new FakeFeed(Manifest(feed, "202610"), BuildArchive(onlyEntry));
        var importer = new FakeImporter(new CatalogueImportState("202609", 1000));

        var outcome = await Build(client, importer).RunAsync(feed, CancellationToken.None);

        outcome.Should().Be(RemoteCatalogueRefreshOutcome.Rejected);
        importer.Imports.Should().BeEmpty();
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("ES")]
    [InlineData("FR")]
    public void ValidateArchive_applies_the_cap_of_the_feed(string country)
    {
        // 128 MB: within the Italian cap, above the 64 MB of the others.
        var feed = Descriptor(country);
        var archive = BuildArchive([.. feed.RequiredEntries]);
        DeclareUncompressedSize(archive, 0x08000000u);

        using (var stream = new MemoryStream(archive))
        {
            var act = () => RemoteCatalogueRefresher.ValidateArchive(stream, feed);
            act.Should().Throw<InvalidDataException>().WithMessage("*limit*");
        }

        var italian = BuildArchive([.. CatalogueFeedDescriptor.Italy.RequiredEntries]);
        DeclareUncompressedSize(italian, 0x08000000u);
        using (var stream = new MemoryStream(italian))
        {
            var act = () => RemoteCatalogueRefresher.ValidateArchive(stream, CatalogueFeedDescriptor.Italy);
            act.Should().NotThrow();
        }
    }

    private static CatalogueFeedDescriptor Descriptor(string country) =>
        CatalogueFeedDescriptor.All.Single(feed => feed.Country.Value == country);

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
        Manifest(CatalogueFeedDescriptor.Italy, version, sha256, size);

    private static CatalogueFeedManifest Manifest(
        CatalogueFeedDescriptor feed, string version, string? sha256 = null, long? size = null) =>
        new(version, feed.FileNameFor(version), null, sha256, size) { Country = feed.Country.Value };

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

    private static byte[] BuildArchive(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                Write(zip, entry, "content\n");
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

        public Exception? ManifestFailure { get; init; }

        public List<string> Requests { get; } = new();

        public List<string> DownloadPaths { get; } = new();

        public Task<CatalogueFeedManifest?> GetLatestAsync(CatalogueFeedDescriptor feed, CancellationToken cancellationToken)
        {
            Requests.Add(feed.Country.Value);
            return ManifestFailure is null ? Task.FromResult(manifest) : Task.FromException<CatalogueFeedManifest?>(ManifestFailure);
        }

        public async Task<CatalogueFeedDownload> DownloadAsync(
            CatalogueFeedDescriptor feed, CatalogueFeedManifest m, string destinationPath, CancellationToken cancellationToken)
        {
            Downloads++;
            DownloadPaths.Add(Path.GetFileName(destinationPath));
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

        public bool BlockImport { get; init; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

            if (BlockImport)
            {
                Entered.TrySetResult();
                await Release.Task;
            }

            using var copy = new MemoryStream();
            await snapshot.CopyToAsync(copy, cancellationToken);
            Imports.Add((expectedCountry.Value, snapshotVersion, minimumRowCount, copy.Length));
            return new ImportReport(1, 0, 0, 0, snapshotVersion, DateTimeOffset.UnixEpoch);
        }

        public List<string> StateRequests { get; } = new();

        public Task<CatalogueImportState> GetImportStateAsync(CountryCode country, CancellationToken cancellationToken)
        {
            StateRequests.Add(country.Value);
            return Task.FromResult(state);
        }
    }
}
