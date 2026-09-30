using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Sync;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Sync;

// B.1, P8 (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §2, §4.2): the
// profile's display name and notification recipients are replicated
// last-writer-wins registers, projected into each device's local copy.
public sealed class ProfileSettingsSyncTests : IDisposable
{
    private const string Passphrase = "sync passphrase for tests";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-profile-" + Guid.NewGuid().ToString("N"));
    private readonly List<SyncDevice> _devices = new();

    public ProfileSettingsSyncTests() => Directory.CreateDirectory(_root);

    private string Folder => Path.Combine(_root, "remote");

    public void Dispose()
    {
        foreach (var d in _devices) d.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_device_that_joins_takes_the_name_and_recipients_of_the_group()
    {
        var a = await CreateGroupAsync(beforeSync: d => d.ProfileSettings.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.DisplayName] = "Mario",
            [ProfileSetting.ToAddress] = "mario@example.org",
        }));

        var b = await JoinAsync(a, "B");
        await b.SyncAsync();

        b.ProfileSettings.Read()[ProfileSetting.DisplayName].Should().Be("Mario");
        b.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().Be("mario@example.org");
    }

    [Fact]
    public async Task Changed_recipients_reach_the_other_device()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");

        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "patient@example.org", "carer@example.org", "doctor@example.org", CancellationToken.None));
        var published = await a.SyncAsync();
        var received = await b.SyncAsync();

        published.OperationsPublished.Should().Be(3);
        received.OperationsApplied.Should().Be(3);
        var values = b.ProfileSettings.Read();
        values[ProfileSetting.ToAddress].Should().Be("patient@example.org");
        values[ProfileSetting.CaregiverAddress].Should().Be("carer@example.org");
        values[ProfileSetting.DoctorAddress].Should().Be("doctor@example.org");
    }

    [Fact]
    public async Task Saving_unchanged_recipients_records_nothing()
    {
        var a = await CreateGroupAsync();

        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            string.Empty, string.Empty, string.Empty, CancellationToken.None));

        (await a.SyncAsync()).OperationsPublished.Should().Be(0);
    }

    [Fact]
    public async Task A_group_created_before_the_settings_were_replicated_converges_on_the_first_save()
    {
        var a = await CreateGroupAsync();
        // As in a group created by an older build: no profile versions.
        await a.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            db.SyncFieldVersions.RemoveRange(db.SyncFieldVersions.Where(v => v.EntityId == Guid.Empty));
            await db.SaveChangesAsync();
        });
        var b = await JoinAsync(a, "B");
        b.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.CaregiverAddress] = "only-on-b@example.org" });

        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "patient@example.org", string.Empty, string.Empty, CancellationToken.None));
        (await a.SyncAsync()).OperationsPublished.Should().Be(3);
        await b.SyncAsync();

        b.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().Be("patient@example.org");
        b.ProfileSettings.Read()[ProfileSetting.CaregiverAddress].Should().BeEmpty();
    }

    [Fact]
    public async Task An_unchanged_save_in_a_group_created_before_the_settings_were_replicated_reaches_a_new_device()
    {
        var a = await CreateGroupAsync(beforeSync: d => d.ProfileSettings.Write(new Dictionary<string, string?>
        {
            [ProfileSetting.ToAddress] = "patient@example.org",
            [ProfileSetting.DoctorAddress] = "doctor@example.org",
        }));
        // As in a group created by an older build: no profile versions on
        // any device (the genesis image on the storage still has them, so
        // they are removed on B too before its first run).
        await RemoveProfileVersionsAsync(a);
        var b = await JoinAsync(a, "B");
        await RemoveProfileVersionsAsync(b);
        await b.SyncAsync();
        b.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().BeEmpty();

        // The administrator saves the same addresses again.
        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "patient@example.org", string.Empty, "doctor@example.org", CancellationToken.None));
        (await a.SyncAsync()).OperationsPublished.Should().Be(3);
        await b.SyncAsync();

        var values = b.ProfileSettings.Read();
        values[ProfileSetting.ToAddress].Should().Be("patient@example.org");
        values[ProfileSetting.CaregiverAddress].Should().BeEmpty();
        values[ProfileSetting.DoctorAddress].Should().Be("doctor@example.org");

        // Once recorded, a second unchanged save records nothing.
        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "patient@example.org", string.Empty, "doctor@example.org", CancellationToken.None));
        (await a.SyncAsync()).OperationsPublished.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_changes_converge_on_the_later_one()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");

        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "old@example.org", string.Empty, string.Empty, CancellationToken.None));
        b.Clock.Advance(TimeSpan.FromMinutes(5));
        await b.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "new@example.org", string.Empty, string.Empty, CancellationToken.None));
        for (var round = 0; round < 2; round++)
        {
            await a.SyncAsync();
            await b.SyncAsync();
        }

        a.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().Be("new@example.org");
        b.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().Be("new@example.org");
    }

    [Fact]
    public async Task A_renamed_profile_is_renamed_on_the_other_device()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");

        // What RenameProfile records for the current profile.
        await a.RunAsync(async sp =>
        {
            await sp.GetRequiredService<IOperationLog>().AppendAsync(
                [new ProfileSettingChanged(ProfileSetting.DisplayName, "Nonna Lucia")], CancellationToken.None);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        });
        await a.SyncAsync();
        await b.SyncAsync();

        b.ProfileSettings.Read()[ProfileSetting.DisplayName].Should().Be("Nonna Lucia");
    }

    [Fact]
    public async Task A_local_copy_left_behind_is_repaired_by_the_next_run()
    {
        var a = await CreateGroupAsync();
        var b = await JoinAsync(a, "B");
        await a.RunAsync(sp => sp.GetRequiredService<UpdateNotificationSettings>().ExecuteAsync(
            "patient@example.org", string.Empty, string.Empty, CancellationToken.None));
        await a.SyncAsync();
        await b.SyncAsync();

        // As after a crash between the apply and the file write.
        b.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.ToAddress] = "stale@example.org" });
        await b.SyncAsync();

        b.ProfileSettings.Read()[ProfileSetting.ToAddress].Should().Be("patient@example.org");
    }

    private async Task<SyncDevice> CreateGroupAsync(Action<SyncDevice>? beforeSync = null)
    {
        var device = Track(new SyncDevice("A", Path.Combine(_root, "A.db"), Now, settings: null));
        await device.InitializeAsync();
        await device.RunAsync(sp => sp.GetRequiredService<AddMedicine>().ExecuteAsync(new AddMedicineCommand(
            "Enalapril", "tablet", 1m, 2, new DateOnly(2026, 9, 1), 7, NotificationChannels.Windows,
            InitialQuantity: 60m), CancellationToken.None));
        beforeSync?.Invoke(device);
        await device.RunAsync(sp => sp.GetRequiredService<CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), Passphrase.ToCharArray(), SyncTarget.ForFolder(Folder),
            CancellationToken.None, SyncFileFormatTests.FastKdf));
        return device;
    }

    private async Task<SyncDevice> JoinAsync(SyncDevice via, string name)
    {
        var path = Path.Combine(_root, $"{name}.db");
        var joined = await via.RunAsync(sp => sp.GetRequiredService<JoinSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), via.Settings.Load()!.GroupId, Passphrase.ToCharArray(), path,
            SyncTarget.ForFolder(Folder), CancellationToken.None));
        var device = Track(new SyncDevice(name, path, via.Clock.GetUtcNow(), joined.Settings, joined.Key));
        await device.InitializeAsync();
        return device;
    }

    private static Task RemoveProfileVersionsAsync(SyncDevice device)
        => device.RunAsync(async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            db.SyncFieldVersions.RemoveRange(db.SyncFieldVersions.Where(v => v.EntityId == Guid.Empty));
            await db.SaveChangesAsync();
        });

    private SyncDevice Track(SyncDevice device)
    {
        _devices.Add(device);
        return device;
    }
}
