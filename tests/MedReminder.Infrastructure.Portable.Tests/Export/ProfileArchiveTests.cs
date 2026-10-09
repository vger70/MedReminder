using FluentAssertions;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// ProfileArchive: the stream-based .mrz export and import of one
// profile for the mobile host (Android backlog B1-06), over the same
// writer, reader, database builder and swap as the Windows services.
public sealed class ProfileArchiveTests : IDisposable
{
    private const string Passphrase = "correct horse battery";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-archive-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static ProfileArchive Archive()
    {
        var cipher = new ArchiveCipher();
        return new ProfileArchive(cipher, new ArchiveReader(cipher), new DatabaseExclusiveAccess(), new FixedClock(Now));
    }

    private ProfileArchiveProfile Profile(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        return new ProfileArchiveProfile(
            "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", "Anna", ProfileRole.Admin,
            Path.Combine(directory, "medreminder.db"),
            Path.Combine(directory, "notifications.settings.json"));
    }

    private static async Task SeedAsync(ProfileArchiveProfile profile, string medicineName)
    {
        var payload = TestArchiveWriter.SamplePayload();
        payload.Medicines[0].Name = medicineName;
        await ProfileDatabaseBuilder.BuildAsync(profile.DatabasePath, payload, CancellationToken.None);
        SqliteConnection.ClearAllPools();
    }

    private static async Task<List<string>> MedicineNamesAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<MedReminderDbContext>()
            .UseSqlite(SqliteConnectionStrings.ForFile(databasePath)).Options;
        await using var db = new MedReminderDbContext(options);
        var names = await db.Medicines.Select(m => m.Name).ToListAsync();
        SqliteConnection.ClearAllPools();
        return names;
    }

    private async Task<byte[]> ExportAsync(ProfileArchiveProfile profile)
    {
        using var buffer = new MemoryStream();
        await Archive().ExportAsync(profile, "1.2.3", buffer, Passphrase.ToCharArray(), _root, CancellationToken.None);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Export_writes_the_profile_and_its_notification_settings()
    {
        var profile = Profile("source");
        await SeedAsync(profile, "Amlodipina");
        ExportSettingsFiles.WriteNotificationSettings(profile.NotificationSettingsPath,
            new ExportedNotificationSettings { PackageExpiryLeadDays = "45" });

        var bytes = await ExportAsync(profile);

        using var archive = Archive().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray());
        archive.Manifest.ProfileId.Should().Be(profile.Id);
        archive.Manifest.AppVersion.Should().Be("1.2.3");
        archive.Manifest.CreatedAtUtc.Should().Be(Now);
        archive.Manifest.Source.Should().BeNull();
        archive.Manifest.Includes.SmtpCredential.Should().BeFalse();
        archive.Payload.Profile.DisplayName.Should().Be("Anna");
        archive.Payload.Medicines.Should().ContainSingle(m => m.Name == "Amlodipina");
        archive.Payload.StockMovements.Should().HaveCount(2);
        archive.Payload.NotificationSettings!.PackageExpiryLeadDays.Should().Be("45");
        Directory.EnumerateDirectories(_root, "export-*").Should().BeEmpty("the snapshot is deleted");
    }

    [Fact]
    public async Task A_short_passphrase_is_rejected_before_anything_is_written()
    {
        var profile = Profile("source");
        await SeedAsync(profile, "Amlodipina");
        using var buffer = new MemoryStream();

        var act = () => Archive().ExportAsync(profile, "1.2.3", buffer, "short".ToCharArray(), _root, CancellationToken.None);

        (await act.Should().ThrowAsync<ExportValidationException>())
            .Which.Reason.Should().Be(ExportValidationReason.PassphraseTooShort);
        buffer.Length.Should().Be(0);
    }

    [Fact]
    public async Task Replace_swaps_in_the_archive_and_keeps_the_previous_database()
    {
        var source = Profile("source");
        await SeedAsync(source, "Amlodipina");
        ExportSettingsFiles.WriteNotificationSettings(source.NotificationSettingsPath,
            new ExportedNotificationSettings { PackageExpiryLeadDays = "45" });
        var bytes = await ExportAsync(source);

        var target = Profile("target");
        await SeedAsync(target, "Metformina");

        using (var archive = Archive().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
        {
            await Archive().ReplaceAsync(archive, target, CancellationToken.None);
        }

        (await MedicineNamesAsync(target.DatabasePath)).Should().Equal("Amlodipina");
        var backup = Directory.EnumerateFiles(Path.GetDirectoryName(target.DatabasePath)!, "medreminder.db.bak-*").Single();
        (await MedicineNamesAsync(backup)).Should().Equal("Metformina");
        ExportSettingsFiles.ReadNotificationSettings(target.NotificationSettingsPath)!
            .PackageExpiryLeadDays.Should().Be("45");
        Directory.EnumerateFiles(Path.GetDirectoryName(target.DatabasePath)!, "medreminder.import-*").Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_replace_leaves_the_database_untouched()
    {
        var source = Profile("source");
        await SeedAsync(source, "Amlodipina");
        var bytes = await ExportAsync(source);
        var target = Profile("target");
        await SeedAsync(target, "Metformina");

        using (var archive = Archive().Decrypt(new MemoryStream(bytes), Passphrase.ToCharArray()))
        {
            var act = () => Archive().ReplaceAsync(archive, target, new CancellationToken(canceled: true));
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        (await MedicineNamesAsync(target.DatabasePath)).Should().Equal("Metformina");
        var directory = Path.GetDirectoryName(target.DatabasePath)!;
        Directory.EnumerateFiles(directory, "medreminder.db.bak-*").Should().BeEmpty();
        Directory.EnumerateFiles(directory, "medreminder.import-*").Should().BeEmpty();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
