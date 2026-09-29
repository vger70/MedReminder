using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Household.Remote;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Household;
using MedReminder.Infrastructure.Sync;
using MedReminder.Infrastructure.Tests.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Household;

// Household step H3a end to end: two installations, each with its own
// household store on SQLite, share a household through a folder, with the
// real envelope, key wrap and cipher.
public sealed class HouseholdSyncTests : IDisposable
{
    private const string Passphrase = "household passphrase";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mr-household-sync-" + Guid.NewGuid().ToString("N"));

    private string Folder => Path.Combine(_root, "remote");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Installation Create(string name) => new(Path.Combine(_root, name), name);

    private async Task<(Installation A, Installation B)> PublishedAndJoinedAsync()
    {
        var a = Create("A");
        a.Registry.Add("admin", "Anna", ProfileRole.Admin);
        a.Registry.Add("user", "Bruno", ProfileRole.User);
        await a.ReconcileAsync();
        await a.Smtp().ExecuteAsync(new SmtpTransport("smtp.example.org", 587, true, "anna", "anna@example.org", "MR", 30),
            "smtp secret", false, CancellationToken.None);
        await a.Sync.PublishAsync(SyncTarget.ForFolder(Folder), Passphrase.ToCharArray(), "PC A", CancellationToken.None,
            SyncFileFormatTests.FastKdf);

        var b = Create("B");
        b.Registry.Add("user", "Bruno (old name)", ProfileRole.User);
        var householdId = (await HouseholdFile.ListAsync(new LocalFolderSyncTransport(Folder), CancellationToken.None)).Single();
        (await b.Sync.JoinAsync(SyncTarget.ForFolder(Folder), householdId, Passphrase.ToCharArray(), "PC B",
            CancellationToken.None)).Problems.Should().BeEmpty();
        return (a, b);
    }

    [Fact]
    public async Task A_joining_installation_takes_the_household_profiles_and_settings()
    {
        var (_, b) = await PublishedAndJoinedAsync();

        b.Settings.Smtp.Host.Should().Be("smtp.example.org");
        b.Credentials.Password.Should().Be("smtp secret");
        b.Registry.GetById("user")!.DisplayName.Should().Be("Bruno");
        b.Registry.GetById("admin").Should().BeNull("sync never creates a profile on this device");
        (await b.Log.ProfilesAsync(CancellationToken.None)).Select(p => p.ProfileId).Should().BeEquivalentTo("admin", "user");
    }

    [Fact]
    public async Task Changes_on_either_side_reach_the_other()
    {
        var (a, b) = await PublishedAndJoinedAsync();

        await new ChangeProfileRole(a.Registry, a.As("admin", ProfileRole.Admin), a.Log,
            NullLogger<ChangeProfileRole>.Instance).ExecuteAsync("user", ProfileRole.Admin, CancellationToken.None);
        (await a.Sync.RunAsync(CancellationToken.None)).OperationsPublished.Should().Be(1);
        await b.Sync.RunAsync(CancellationToken.None);
        b.Registry.GetById("user")!.Role.Should().Be(ProfileRole.Admin);

        await b.Smtp(b.As("user", ProfileRole.Admin)).ExecuteAsync(
            b.Settings.Smtp with { Port = 465 }, "new secret", false, CancellationToken.None);
        await b.Sync.RunAsync(CancellationToken.None);
        var result = await a.Sync.RunAsync(CancellationToken.None);

        result.Problems.Should().BeEmpty();
        a.Settings.Smtp.Port.Should().Be(465);
        a.Credentials.Password.Should().Be("new secret");
    }

    [Fact]
    public async Task Concurrent_changes_converge_on_the_latest()
    {
        var (a, b) = await PublishedAndJoinedAsync();
        await a.General().ExecuteAsync(new UserSettings { ReferenceCountry = "FR" }, CancellationToken.None);
        await Task.Delay(5);
        await b.General(b.As("user", ProfileRole.Admin)).ExecuteAsync(new UserSettings { ReferenceCountry = "DE" },
            CancellationToken.None);

        for (var round = 0; round < 2; round++)
        {
            await a.Sync.RunAsync(CancellationToken.None);
            await b.Sync.RunAsync(CancellationToken.None);
        }

        a.Settings.User.ReferenceCountry.Should().Be("DE");
        b.Settings.User.ReferenceCountry.Should().Be("DE");
        (await a.Log.SettingsAsync(CancellationToken.None)).Should().BeEquivalentTo(
            await b.Log.SettingsAsync(CancellationToken.None), "both hold the same versions");
    }

    [Fact]
    public async Task The_smtp_password_never_appears_in_clear_in_the_storage_or_the_local_log()
    {
        var (a, b) = await PublishedAndJoinedAsync();
        await a.Sync.RunAsync(CancellationToken.None);

        var clear = Encoding.UTF8.GetBytes("smtp secret");
        foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(a.Directory, "*", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(b.Directory, "*", SearchOption.AllDirectories)))
        {
            File.ReadAllBytes(file).AsSpan().IndexOf(clear).Should().Be(-1, file);
        }
    }

    [Fact]
    public async Task A_wrong_passphrase_does_not_join()
    {
        var (a, _) = await PublishedAndJoinedAsync();
        var c = Create("C");
        var identity = await a.Store.EnsureCreatedAsync(CancellationToken.None);

        var act = () => c.Sync.JoinAsync(SyncTarget.ForFolder(Folder), identity.HouseholdId, "wrong words".ToCharArray(),
            "PC C", CancellationToken.None);

        await act.Should().ThrowAsync<CryptographicException>();
        (await c.Log.ProfilesAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_household_is_published_once()
    {
        var (a, _) = await PublishedAndJoinedAsync();

        var act = () => a.Sync.PublishAsync(SyncTarget.ForFolder(Folder), Passphrase.ToCharArray(), "PC A",
            CancellationToken.None, SyncFileFormatTests.FastKdf);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_profile_group_listing_does_not_see_the_household()
    {
        await PublishedAndJoinedAsync();

        (await MedReminder.Application.Sync.JoinSyncGroup.ListGroupsAsync(new LocalFolderSyncTransport(Folder),
            CancellationToken.None)).Should().BeEmpty();
    }

    // One installation: its household store and key in its own folder, and
    // in-memory stand-ins for profiles.json and the settings files.
    private sealed class Installation
    {
        public Installation(string directory, string name)
        {
            Directory = directory;
            var household = Path.Combine(directory, "household");
            Store = new SqliteHouseholdStore(household);
            Log = new HouseholdLog(Store, TimeProvider.System);
            var projection = new HouseholdProjection(Log, Registry, Settings, Credentials, Protector);
            Sync = new HouseholdSync(Store, new ProtectedHouseholdKeyStore(Protector, household), new FolderTransports(),
                new ArchiveCipher(), Protector, projection, TimeProvider.System,
                new MedReminder.Application.Sync.SyncEngineOptions { DeviceName = name });
        }

        public string Directory { get; }
        public SqliteHouseholdStore Store { get; }
        public HouseholdLog Log { get; }
        public HouseholdSync Sync { get; }
        public FakeRegistry Registry { get; } = new();
        public FakeSettings Settings { get; } = new();
        public FakeCredentials Credentials { get; } = new();
        public XorProtector Protector { get; } = new();

        public ICurrentProfile As(string id, ProfileRole role) => new Current(id, role);

        public Task ReconcileAsync()
            => new ReconcileHousehold(Registry, Log, Settings, Credentials, Protector).ExecuteAsync(CancellationToken.None);

        public UpdateSmtpSettings Smtp(ICurrentProfile? current = null)
            => new(Settings, Credentials, Protector, current ?? As("admin", ProfileRole.Admin), Log);

        public UpdateGeneralSettings General(ICurrentProfile? current = null)
            => new(Settings, current ?? As("admin", ProfileRole.Admin), Log);
    }

    private sealed class FolderTransports : ISyncTransportFactory
    {
        public ISyncTransport Create(SyncTarget target) => new LocalFolderSyncTransport(target.Folder!);
    }

    // Stands for DPAPI: reversible, and never the clear text on disk.
    private sealed class XorProtector : ICredentialProtector
    {
        public string Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x5A)).ToArray());

        public string Unprotect(string ciphertext)
            => Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext).Select(b => (byte)(b ^ 0x5A)).ToArray());
    }

    private sealed class Current(string id, ProfileRole role) : ICurrentProfile
    {
        public string Id => id;
        public string DisplayName => id;
        public ProfileRole Role => role;
        public bool IsAdmin => role == ProfileRole.Admin;
        public string DataDirectory => string.Empty;
        public string DatabasePath => string.Empty;
        public string NotificationSettingsPath => string.Empty;
    }

    private sealed class FakeSettings : IInstallationSettingsStore
    {
        public SmtpTransport Smtp { get; set; } = new(string.Empty, 587, true, string.Empty, string.Empty, "MedReminder", 30);
        public BackupSettings Backup { get; set; } = new();
        public UserSettings User { get; set; } = new();
        public SmtpTransport ReadSmtp() => Smtp;
        public void WriteSmtp(SmtpTransport settings) => Smtp = settings;
        public BackupSettings ReadBackup() => Backup;
        public void WriteBackup(BackupSettings settings) => Backup = settings;
        public UserSettings ReadUser() => User;
        public void WriteUser(UserSettings settings) => User = settings;
    }

    private sealed class FakeCredentials : ISmtpCredentialStore
    {
        public string? Password { get; set; }
        public bool HasPassword => Password is not null;
        public string? GetPassword() => Password;
        public void SetPassword(string password) => Password = password;
        public void Clear() => Password = null;
    }

    private sealed class FakeRegistry : IProfileRegistry
    {
        private readonly List<(string Id, string Name, ProfileRole Role, ProfilePinHash? Pin)> _entries = [];

        public string? ActiveProfileIdHint => null;

        public void Add(string id, string name, ProfileRole role) => _entries.Add((id, name, role, null));

        public IReadOnlyList<Profile> ListProfiles() => [.. _entries.Select(e => ToProfile(e))];

        public Profile? GetById(string id) => _entries.FirstOrDefault(e => e.Id == id) is { Id: not null } e ? ToProfile(e) : null;

        public void Rename(string id, string newDisplayName) => Update(id, e => e with { Name = newDisplayName.Trim() });

        public void SetRole(string id, ProfileRole role)
        {
            if (role != ProfileRole.Admin && !_entries.Any(e => e.Id != id && e.Role == ProfileRole.Admin))
                throw new InvalidOperationException("Cannot demote the last admin profile.");
            Update(id, e => e with { Role = role });
        }

        public ProfilePinHash? GetPinHash(string id) => _entries.Single(e => e.Id == id).Pin;

        public void SetPinHash(string id, ProfilePinHash? pin) => Update(id, e => e with { Pin = pin });

        public void SetPin(string id, string? pin)
            => SetPinHash(id, pin is null ? null : new ProfilePinHash("h" + pin, "salt", 100_000));

        public bool HasPin(string id) => GetPinHash(id) is not null;
        public bool VerifyPin(string id, string pin) => throw new NotSupportedException();
        public Profile Create(string displayName, ProfileRole role) => throw new NotSupportedException();
        public void Delete(string id, bool deleteData) => throw new NotSupportedException();
        public void SetActiveProfileHint(string id) { }

        private void Update(string id, Func<(string Id, string Name, ProfileRole Role, ProfilePinHash? Pin),
            (string Id, string Name, ProfileRole Role, ProfilePinHash? Pin)> change)
        {
            var i = _entries.FindIndex(e => e.Id == id);
            if (i < 0) throw new InvalidOperationException("Unknown profile.");
            _entries[i] = change(_entries[i]);
        }

        private static Profile ToProfile((string Id, string Name, ProfileRole Role, ProfilePinHash? Pin) e)
            => new(e.Id, e.Name, e.Role, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, e.Pin is not null);
    }
}
