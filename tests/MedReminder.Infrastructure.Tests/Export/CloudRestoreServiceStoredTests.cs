using System.Runtime.Versioning;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// Restore from snapshots held by a provider API (C.3++ Phase 2, B.1
// Phase 4a): the list comes from the C.3+ file names, and the chosen
// archive is downloaded under %LOCALAPPDATA%\MedReminder, imported, then
// deleted.
[SupportedOSPlatform("windows")]
public sealed class CloudRestoreServiceStoredTests
{
    private sealed class MemoryStorage(Dictionary<string, (byte[] Content, DateTimeOffset At)> archives) : IArchiveStorage
    {
        public Task<string> UploadAsync(Stream archive, string suggestedName, CancellationToken ct) => throw new NotSupportedException();

        public Task<Stream> DownloadAsync(string id, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream(archives[id].Content));

        public Task<IReadOnlyList<ArchiveInfo>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ArchiveInfo>>(
                [.. archives.Select(a => new ArchiveInfo(a.Key, a.Key, a.Value.At, a.Value.Content.Length))]);

        public Task DeleteAsync(string id, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingImport : IImportService
    {
        public string? Path { get; private set; }
        public byte[]? Content { get; private set; }

        public Task<ExportManifest> ReadManifestAsync(string archivePath, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ImportAsync(string archivePath, char[] passphrase, ImportOptions options, IProgress<int>? progress,
            CancellationToken cancellationToken)
        {
            Path = archivePath;
            Content = File.ReadAllBytes(archivePath);
            return Task.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Stored = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshots_are_listed_from_their_names_newest_first()
    {
        var storage = new MemoryStorage(new()
        {
            ["medreminder-abc-def-20260920-080000.mrz"] = ([1], Stored),
            ["medreminder-abc-def-20260921-080000.mrz"] = ([2], Stored),
            ["copied-by-hand.mrz"] = ([3], Stored),
        });
        var service = new CloudRestoreService(new RecordingImport(), NullLogger<CloudRestoreService>.Instance);

        var list = await service.ListStoredSnapshotsAsync(storage, CancellationToken.None);

        list.Select(s => s.FileName).Should().Equal(
            "medreminder-abc-def-20260921-080000.mrz", "medreminder-abc-def-20260920-080000.mrz", "copied-by-hand.mrz");
        list[0].ProfileId.Should().Be("abc-def");
        list[0].CreatedAtUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 8, 0, 0, TimeSpan.Zero));
        list[2].ProfileId.Should().BeEmpty();
        list[2].CreatedAtUtc.Should().Be(Stored);
    }

    [Fact]
    public async Task The_chosen_snapshot_is_downloaded_under_the_app_folder_then_deleted()
    {
        var storage = new MemoryStorage(new() { ["medreminder-p-20260921-080000.mrz"] = ([7, 8, 9], Stored) });
        var import = new RecordingImport();
        var service = new CloudRestoreService(import, NullLogger<CloudRestoreService>.Instance);

        await service.RestoreStoredAsync(storage, "medreminder-p-20260921-080000.mrz", "x".ToCharArray(),
            new ImportOptions(), null, CancellationToken.None);

        import.Content.Should().Equal(7, 8, 9);
        import.Path.Should().StartWith(AppDataPaths.GetAppDataDirectory());
        File.Exists(import.Path).Should().BeFalse();
    }
}
