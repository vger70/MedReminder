using FluentAssertions;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Backup;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Backup;

// One scheduled cloud backup over ProfileArchive (Android backlog B2-03):
// automatic snapshots per profile, the desktop's failure rules, retention
// limited to the profiles backed up, nothing left in the scratch folder.
public sealed class CloudBackupRunTests : IDisposable
{
    private const string Passphrase = "correct horse battery";
    private const string Anna = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Bruno = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 3, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-cloud-run-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryArchiveStorage _storage = new() { UploadTime = Now };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Scratch => Path.Combine(_root, "scratch");

    private static ProfileArchive Archive()
    {
        var cipher = new ArchiveCipher();
        return new ProfileArchive(cipher, new ArchiveReader(cipher), new DatabaseExclusiveAccess(), new FixedClock(Now));
    }

    private async Task<ProfileArchiveProfile> ProfileAsync(string id, string name)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        var profile = new ProfileArchiveProfile(id, name, ProfileRole.Admin,
            Path.Combine(directory, "medreminder.db"), Path.Combine(directory, "notifications.settings.json"));
        await ProfileDatabaseBuilder.BuildAsync(profile.DatabasePath, TestArchiveWriter.SamplePayload(), CancellationToken.None);
        SqliteConnection.ClearAllPools();
        return profile;
    }

    private Task<CloudBackupResult> RunAsync(IReadOnlyList<ProfileArchiveProfile> profiles, int retentionDays = 30)
        => new CloudBackupRun(Archive(), new FixedClock(Now)).RunAsync(
            _storage, profiles, Passphrase.ToCharArray(), "1.2.3", "Pixel 8", Scratch, retentionDays, CancellationToken.None);

    [Fact]
    public async Task Every_profile_is_stored_as_an_automatic_snapshot()
    {
        var profiles = new[] { await ProfileAsync(Anna, "Anna"), await ProfileAsync(Bruno, "Bruno") };

        var result = await RunAsync(profiles);

        result.Succeeded.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.SignInRequired.Should().BeFalse();
        _storage.Names.Should().BeEquivalentTo(CloudSnapshotName.Create(Anna, Now), CloudSnapshotName.Create(Bruno, Now));

        using var archive = Archive().Decrypt(new MemoryStream(_storage.Bytes(result.ArchiveIds[0])), Passphrase.ToCharArray());
        archive.Manifest.ProfileId.Should().Be(Anna);
        archive.Manifest.Source.Should().Be(AutomaticArchiveSource.Source);
        archive.Manifest.Device!.HostNameSha256.Should().Be(AutomaticArchiveSource.HashDeviceName("Pixel 8"));
        archive.Manifest.Device.ProfileId.Should().Be(Anna);
        Directory.EnumerateFileSystemEntries(Scratch).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_profile_does_not_stop_the_others()
    {
        var profiles = new[] { await ProfileAsync(Anna, "Anna"), await ProfileAsync(Bruno, "Bruno") };
        _storage.FailUpload = name => name.Contains(Anna) ? new IOException("quota") : null;

        var result = await RunAsync(profiles);

        result.ArchiveIds.Should().HaveCount(1);
        result.Errors.Should().ContainSingle().Which.Should().Be($"cloud {Anna}: quota");
        _storage.Names.Should().BeEquivalentTo(CloudSnapshotName.Create(Bruno, Now));
        Directory.EnumerateFileSystemEntries(Scratch).Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_that_needs_a_new_sign_in_stops_the_run()
    {
        var profiles = new[] { await ProfileAsync(Anna, "Anna"), await ProfileAsync(Bruno, "Bruno") };
        _storage.FailUpload = _ => new CloudSignInRequiredException(CloudProvider.GoogleDrive);

        var result = await RunAsync(profiles);

        result.Succeeded.Should().BeFalse();
        result.SignInRequired.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        _storage.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task Retention_prunes_only_the_profiles_backed_up()
    {
        var profiles = new[] { await ProfileAsync(Anna, "Anna"), await ProfileAsync(Bruno, "Bruno") };
        var oldAnna = CloudSnapshotName.Create(Anna, Now.AddDays(-40));
        var oldBruno = CloudSnapshotName.Create(Bruno, Now.AddDays(-40));
        _storage.Add(oldAnna, Now.AddDays(-40));
        _storage.Add(oldBruno, Now.AddDays(-40));
        _storage.FailUpload = name => name.Contains(Bruno) ? new IOException("quota") : null;

        var result = await RunAsync(profiles);

        result.Pruned.Should().Be(1);
        _storage.Names.Should().BeEquivalentTo(CloudSnapshotName.Create(Anna, Now), oldBruno);
    }

    [Fact]
    public async Task Nothing_backed_up_prunes_nothing()
    {
        var profiles = new[] { await ProfileAsync(Anna, "Anna") };
        var old = CloudSnapshotName.Create(Anna, Now.AddDays(-40));
        _storage.Add(old, Now.AddDays(-40));
        _storage.FailUpload = _ => new IOException("offline");

        var result = await RunAsync(profiles);

        result.Pruned.Should().Be(0);
        _storage.Names.Should().BeEquivalentTo(old);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
