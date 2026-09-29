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
using Microsoft.Extensions.DependencyInjection;
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

    // Step H3b: keys.
    private static readonly ProfileGroupKey UserGroup =
        new(Guid.Parse("0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f"), 2, RandomNumberGenerator.GetBytes(32));

    private async Task<(Installation A, Installation B)> PublishedWithASyncedProfileAsync()
    {
        var a = Create("A");
        a.Registry.Add("admin", "Anna", ProfileRole.Admin);
        a.Registry.Add("user", "Bruno", ProfileRole.User);
        a.GroupKeys.Keys["user"] = UserGroup;
        await a.ReconcileAsync();
        await a.Sync.PublishAsync(SyncTarget.ForFolder(Folder), Passphrase.ToCharArray(), "PC A", CancellationToken.None,
            SyncFileFormatTests.FastKdf);
        var b = Create("B");
        var householdId = (await a.Store.EnsureCreatedAsync(CancellationToken.None)).HouseholdId;
        await b.Sync.JoinAsync(SyncTarget.ForFolder(Folder), householdId, Passphrase.ToCharArray(), "PC B",
            CancellationToken.None);
        return (a, b);
    }

    [Fact]
    public async Task Publishing_adopts_the_synced_profiles_with_a_grant_to_itself_and_an_escrow()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();

        (await a.Keyring.OpenGrantAsync("user", CancellationToken.None))!.Key.Should().Equal(UserGroup.Key);
        (await a.Keyring.OpenGrantAsync("admin", CancellationToken.None)).Should().BeNull("the profile is not synced");
        var keys = await a.Keyring.KeysAsync(CancellationToken.None);
        keys.Escrows.Should().ContainKey("user");
        keys.RecoveryPublicKey.Should().NotBeNull();
        (await a.Keyring.AdoptProfileGroupsAsync(CancellationToken.None)).Should().Be(0, "already adopted");
    }

    [Fact]
    public async Task A_granted_device_opens_the_profile_key_and_loses_it_when_revoked()
    {
        var (a, b) = await PublishedWithASyncedProfileAsync();
        var deviceB = (await b.Store.EnsureCreatedAsync(CancellationToken.None)).DeviceId;
        (await b.Keyring.OpenGrantAsync("user", CancellationToken.None)).Should().BeNull();

        await a.Sync.RunAsync(CancellationToken.None);
        await a.Keyring.GrantAsync("user", deviceB, CancellationToken.None);
        await a.Sync.RunAsync(CancellationToken.None);
        await b.Sync.RunAsync(CancellationToken.None);

        var opened = await b.Keyring.OpenGrantAsync("user", CancellationToken.None);
        opened!.Key.Should().Equal(UserGroup.Key);
        opened.GroupId.Should().Be(UserGroup.GroupId);
        opened.KeyVersion.Should().Be(2);

        await a.Keyring.RevokeAsync("user", deviceB, CancellationToken.None);
        await a.Sync.RunAsync(CancellationToken.None);
        await b.Sync.RunAsync(CancellationToken.None);
        (await b.Keyring.OpenGrantAsync("user", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task The_household_passphrase_recovers_a_profile_key_from_its_escrow()
    {
        var (_, b) = await PublishedWithASyncedProfileAsync();

        (await b.Sync.RecoverProfileKeyAsync("user", Passphrase.ToCharArray(), CancellationToken.None))!
            .Key.Should().Equal(UserGroup.Key);
        (await b.Sync.RecoverProfileKeyAsync("admin", Passphrase.ToCharArray(), CancellationToken.None))
            .Should().BeNull();
        var wrong = () => b.Sync.RecoverProfileKeyAsync("user", "wrong words".ToCharArray(), CancellationToken.None);
        await wrong.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task A_device_that_joins_publishes_its_key_once()
    {
        var (a, b) = await PublishedWithASyncedProfileAsync();
        var deviceB = (await b.Store.EnsureCreatedAsync(CancellationToken.None)).DeviceId;
        await a.Sync.RunAsync(CancellationToken.None);

        (await a.Keyring.KeysAsync(CancellationToken.None)).DevicePublicKeys.Should().ContainKey(deviceB);
        (await b.Sync.RunAsync(CancellationToken.None)).OperationsPublished.Should().Be(0);
    }

    [Fact]
    public async Task No_profile_key_appears_in_clear_in_the_storage_or_the_household_files()
    {
        var (a, b) = await PublishedWithASyncedProfileAsync();
        await a.Sync.RunAsync(CancellationToken.None);

        var base64 = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(UserGroup.Key));
        foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(a.Directory, "*", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(b.Directory, "*", SearchOption.AllDirectories)))
        {
            var bytes = File.ReadAllBytes(file);
            bytes.AsSpan().IndexOf(UserGroup.Key).Should().Be(-1, file);
            bytes.AsSpan().IndexOf(base64).Should().Be(-1, file);
        }
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

    // Step H3c: pairing codes and the installation join.
    [Fact]
    public void A_household_code_round_trips_and_is_not_a_group_code()
    {
        var code = new HouseholdPairingCode(Guid.NewGuid(), Guid.NewGuid(), CloudProvider.OneDrive, RandomNumberGenerator.GetBytes(32));

        HouseholdPairingCode.TryParse(code.Text, out var parsed).Should().BeTrue();
        parsed!.HouseholdId.Should().Be(code.HouseholdId);
        parsed.DeviceId.Should().Be(code.DeviceId);
        parsed.Provider.Should().Be(CloudProvider.OneDrive);
        parsed.Secret.Should().Equal(code.Secret);
        code.Text.Should().StartWith("mrpair2.");
        code.ToString().Should().NotContain(code.Text.Split('.')[4]);
        MedReminder.Application.Sync.Remote.SyncPairingCode.TryParse(code.Text, out _).Should().BeFalse();
        HouseholdPairingCode.TryParse(code.Text.Replace("mrpair2", "mrpair1"), out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_device_joining_with_a_code_is_granted_the_offered_profiles_only()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();
        var offer = await a.Offers().StartAsync(["user"], CancellationToken.None);
        var c = Create("C");

        var (_, granted) = await c.Sync.JoinAsync(SyncTarget.ForFolder(Folder), offer.Code, "PC C", CancellationToken.None);

        granted.Should().Equal("user");
        (await c.Keyring.OpenGrantAsync("user", CancellationToken.None))!.Key.Should().Equal(UserGroup.Key);
        (await c.Keyring.OpenGrantAsync("admin", CancellationToken.None)).Should().BeNull();
        await a.Sync.RunAsync(CancellationToken.None);
        var deviceC = (await c.Store.EnsureCreatedAsync(CancellationToken.None)).DeviceId;
        (await a.Keyring.KeysAsync(CancellationToken.None)).Grants.Should().ContainKey(("user", deviceC),
            "the grant outlives the offer");
    }

    [Fact]
    public async Task Only_an_admin_offers_and_only_profiles_this_device_holds()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();

        var asUser = () => a.Offers(a.As("user", ProfileRole.User)).StartAsync(["user"], CancellationToken.None);
        await asUser.Should().ThrowAsync<ProfileAdministrationException>();
        var notHeld = () => a.Offers().StartAsync(["admin"], CancellationToken.None);
        await notHeld.Should().ThrowAsync<InvalidOperationException>();
        var unknown = () => a.Offers().StartAsync(["nobody"], CancellationToken.None);
        await unknown.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_ended_or_replaced_offer_does_not_open()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();
        var first = await a.Offers().StartAsync(["user"], CancellationToken.None);
        await a.Offers().StartAsync([], CancellationToken.None);

        var replaced = () => Create("C").Sync.JoinAsync(SyncTarget.ForFolder(Folder), first.Code, "PC C", CancellationToken.None);
        await replaced.Should().ThrowAsync<CryptographicException>();

        var second = await a.Offers().StartAsync(["user"], CancellationToken.None);
        await a.Offers().EndAsync(CancellationToken.None);
        var ended = () => Create("D").Sync.JoinAsync(SyncTarget.ForFolder(Folder), second.Code, "PC D", CancellationToken.None);
        await ended.Should().ThrowAsync<MedReminder.Application.Sync.Remote.SyncPairingExpiredException>();
    }

    [Fact]
    public async Task The_offer_file_holds_no_key_in_clear()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();
        await a.Offers().StartAsync(["user"], CancellationToken.None);

        var base64 = Encoding.ASCII.GetBytes(Convert.ToBase64String(UserGroup.Key));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Folder), "*.mrp", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            bytes.AsSpan().IndexOf(UserGroup.Key).Should().Be(-1);
            bytes.AsSpan().IndexOf(base64).Should().Be(-1);
        }
    }

    [Fact]
    public async Task With_the_passphrase_an_admin_approves_with_the_pin_and_the_escrow_grants()
    {
        var (a, _) = await PublishedWithASyncedProfileAsync();
        var pin = new ProfilePinHash(Convert.ToBase64String(System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "4321", Encoding.ASCII.GetBytes("0123456789abcdef"), 1000, HashAlgorithmName.SHA256, 32)),
            Convert.ToBase64String(Encoding.ASCII.GetBytes("0123456789abcdef")), 1000);
        a.Registry.SetPinHash("admin", pin);
        await a.ReconcileAsync();
        await a.Sync.RunAsync(CancellationToken.None);
        var c = Create("C");
        var join = c.Join(null!);
        var householdId = (await a.Store.EnsureCreatedAsync(CancellationToken.None)).HouseholdId;
        await join.JoinHouseholdAsync(SyncTarget.ForFolder(Folder), householdId, Passphrase.ToCharArray(), "PC C",
            CancellationToken.None);

        var wrongPin = () => join.ApproveAndGrantAsync("admin", "0000", ["user"], Passphrase.ToCharArray(), CancellationToken.None);
        (await wrongPin.Should().ThrowAsync<HouseholdApprovalException>()).Which.Error.Should().Be(HouseholdApprovalError.WrongPin);
        var notAdmin = () => join.ApproveAndGrantAsync("user", null, ["user"], Passphrase.ToCharArray(), CancellationToken.None);
        (await notAdmin.Should().ThrowAsync<HouseholdApprovalException>()).Which.Error.Should().Be(HouseholdApprovalError.NotAdmin);

        (await join.ApproveAndGrantAsync("admin", "4321", ["user", "admin"], Passphrase.ToCharArray(), CancellationToken.None))
            .Should().Equal(["user"], "the admin profile is not synced, so it has no escrow");
        (await c.Keyring.OpenGrantAsync("user", CancellationToken.None))!.Key.Should().Equal(UserGroup.Key);
    }

    [Fact]
    public async Task Joining_an_installation_builds_each_granted_profile_from_its_group()
    {
        Directory.CreateDirectory(Folder);
        using var group = new SyncDevice("P", Path.Combine(_root, "P.db"), DateTimeOffset.UtcNow, settings: null);
        await group.InitializeAsync();
        await group.RunAsync(sp => sp.GetRequiredService<MedReminder.Application.Sync.CreateSyncGroup>().ExecuteAsync(
            new LocalFolderSyncTransport(Folder), "group words".ToCharArray(), SyncTarget.ForFolder(Folder),
            CancellationToken.None, SyncFileFormatTests.FastKdf));
        var settings = group.Settings.Load()!;
        var a = Create("A");
        a.Registry.Add("admin", "Anna", ProfileRole.Admin);
        a.Registry.Add("user", "Bruno", ProfileRole.User);
        a.GroupKeys.Keys["user"] = new ProfileGroupKey(settings.GroupId, settings.KeyVersion,
            group.Keys.Load(settings.GroupId, settings.KeyVersion)!);
        await a.ReconcileAsync();
        await a.Sync.PublishAsync(SyncTarget.ForFolder(Folder), Passphrase.ToCharArray(), "PC A", CancellationToken.None,
            SyncFileFormatTests.FastKdf);
        var offer = await a.Offers().StartAsync(["user"], CancellationToken.None);
        var c = Create("C");

        var result = await group.RunAsync(sp => c.Join(sp.GetRequiredService<MedReminder.Application.Sync.JoinSyncGroup>())
            .JoinWithCodeAsync(SyncTarget.ForFolder(Folder), offer.Code, "PC C", CancellationToken.None));

        result.Profiles.Should().Equal(new JoinedProfile("user", JoinedProfileStatus.Installed));
        c.Registry.GetById("user").Should().BeEquivalentTo(new { DisplayName = "Bruno", Role = ProfileRole.User });
        c.Registry.GetById("admin").Should().BeNull();
        var installed = Path.Combine(c.ProfilesRoot, "user");
        File.Exists(Path.Combine(installed, "medreminder.db")).Should().BeTrue();
        var joined = new JsonSyncSettingsStore(Path.Combine(installed, JsonSyncSettingsStore.FileName)).Load()!;
        joined.GroupId.Should().Be(settings.GroupId);
        joined.DeviceId.Should().NotBe(settings.DeviceId);
        c.InstalledKeys.Values.Single().Load(settings.GroupId, settings.KeyVersion).Should()
            .Equal(group.Keys.Load(settings.GroupId, settings.KeyVersion));
        Directory.EnumerateDirectories(c.ProfilesRoot, ".join-*").Should().BeEmpty();

        // What ProfileGroupKeys reads from the installed folder on Windows.
        c.GroupKeys.Keys["user"] = new ProfileGroupKey(joined.GroupId, joined.KeyVersion,
            c.InstalledKeys.Values.Single().Load(joined.GroupId, joined.KeyVersion)!);
        var again = await group.RunAsync(sp => c.Join(sp.GetRequiredService<MedReminder.Application.Sync.JoinSyncGroup>())
            .InstallGrantedAsync(["user", "admin"], "PC C", CancellationToken.None));
        again.Should().Equal(new JoinedProfile("user", JoinedProfileStatus.AlreadyHere),
            new JoinedProfile("admin", JoinedProfileStatus.NotGranted));
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
            Keyring = new HouseholdKeyring(Log, Store, new ProtectedDeviceKeyStore(Protector, household), GroupKeys,
                Registry, new ArchiveCipher());
            Sync = new HouseholdSync(Store, new ProtectedHouseholdKeyStore(Protector, household), new FolderTransports(),
                new ArchiveCipher(), Protector, projection, Keyring, TimeProvider.System,
                new MedReminder.Application.Sync.SyncEngineOptions { DeviceName = name });
        }

        public string Directory { get; }
        public SqliteHouseholdStore Store { get; }
        public HouseholdLog Log { get; }
        public HouseholdSync Sync { get; }
        public HouseholdKeyring Keyring { get; }
        public FakeGroupKeys GroupKeys { get; } = new();
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

        // Step H3c.
        public HouseholdPairingOffers Offers(ICurrentProfile? current = null)
            => new(Store, new ProtectedHouseholdKeyStore(Protector, System.IO.Path.Combine(Directory, "household")), GroupKeys,
                Log, new FolderTransports(), new ArchiveCipher(), current ?? As("admin", ProfileRole.Admin), TimeProvider.System);

        public Dictionary<string, SyncDevice.MemoryKeyStore> InstalledKeys { get; } = new(StringComparer.Ordinal);

        public string ProfilesRoot => System.IO.Path.Combine(Directory, "profiles");

        public JoinInstallation Join(MedReminder.Application.Sync.JoinSyncGroup join)
            => new(Sync, Keyring, Log, Store, Registry, GroupKeys,
                new HouseholdProfileInstaller(ProfilesRoot, directory =>
                {
                    var keys = new SyncDevice.MemoryKeyStore();
                    InstalledKeys[System.IO.Path.GetFileName(directory)] = keys;
                    return keys;
                }),
                join, new FolderTransports(), NullLogger<JoinInstallation>.Instance);
    }

    // profiles\<id>\sync.protected of each synced profile.
    private sealed class FakeGroupKeys : IProfileGroupKeys
    {
        public Dictionary<string, ProfileGroupKey> Keys { get; } = new(StringComparer.Ordinal);

        public ProfileGroupKey? Load(string profileId)
            => Keys.TryGetValue(profileId, out var key) ? key with { Key = [.. key.Key] } : null;
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

        public void Register(string id, string displayName, ProfileRole role, ProfilePinHash? pin)
        {
            if (_entries.Any(e => e.Id == id)) throw new InvalidOperationException("Profile exists.");
            _entries.Add((id, displayName, role, pin));
        }

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
