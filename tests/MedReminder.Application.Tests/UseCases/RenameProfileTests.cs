using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

// B.1, P8: the display name of the current profile is replicated; another
// profile that takes part in sync is renamed only while it is open.
public class RenameProfileTests
{
    private readonly FakeRegistry _registry = new();
    private readonly RecordingLog _log = new();
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly HashSet<string> _synced = new();

    private RenameProfile Create() => new(_registry, new FakeCurrent("me"), new FakeSyncStatus(_synced), _log, _uow);

    [Fact]
    public async Task Renaming_the_current_profile_records_the_new_name()
    {
        await Create().ExecuteAsync("me", "  Lucia ", CancellationToken.None);

        _registry.Names["me"].Should().Be("Lucia");
        _log.Operations.Should().ContainSingle()
            .Which.Should().Be(new ProfileSettingChanged(ProfileSetting.DisplayName, "Lucia"));
        _uow.SaveChangesCalls.Should().Be(1);
    }

    // Household step H2: the name is also a household register, for the
    // open profile and for another one.
    [Theory]
    [InlineData("me", "Lucia")]
    [InlineData("other", "Paolo")]
    public async Task A_rename_is_recorded_in_the_household(string profileId, string name)
    {
        var store = new InMemoryHouseholdStore();
        var household = new MedReminder.Application.Household.HouseholdLog(store, TimeProvider.System);
        var useCase = new RenameProfile(_registry, new FakeCurrent("me"), new FakeSyncStatus(_synced), _log, _uow, household);

        await useCase.ExecuteAsync(profileId, name, CancellationToken.None);
        await useCase.ExecuteAsync(profileId, name, CancellationToken.None);

        store.Operations.Should().ContainSingle().Which.Type.Should().Be("ProfileRenamed");
    }

    [Fact]
    public async Task Keeping_the_same_name_records_nothing()
    {
        await Create().ExecuteAsync("me", "Me", CancellationToken.None);

        _log.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_profile_is_renamed_locally_without_an_operation()
    {
        await Create().ExecuteAsync("other", "Paolo", CancellationToken.None);

        _registry.Names["other"].Should().Be("Paolo");
        _log.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_synced_profile_must_be_opened_to_be_renamed()
    {
        _synced.Add("other");

        await FluentActions.Awaiting(() => Create().ExecuteAsync("other", "Paolo", CancellationToken.None))
            .Should().ThrowAsync<SyncedProfileRenameException>();
        _registry.Names["other"].Should().Be("Other");
    }

    private sealed class RecordingLog : IOperationLog
    {
        public List<SyncOperationBody> Operations { get; } = new();

        public Task AppendAsync(IReadOnlyList<SyncOperationBody> operations, CancellationToken cancellationToken)
        {
            Operations.AddRange(operations);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSyncStatus(HashSet<string> synced) : ISyncProfileStatus
    {
        public bool IsSyncEnabled(string profileId) => synced.Contains(profileId);
    }

    private sealed class FakeCurrent(string id) : ICurrentProfile
    {
        public string Id => id;
        public string DisplayName => "Me";
        public ProfileRole Role => ProfileRole.Admin;
        public bool IsAdmin => true;
        public string DataDirectory => string.Empty;
        public string DatabasePath => string.Empty;
        public string NotificationSettingsPath => string.Empty;
    }

    private sealed class FakeRegistry : IProfileRegistry
    {
        public Dictionary<string, string> Names { get; } = new() { ["me"] = "Me", ["other"] = "Other" };

        public string? ActiveProfileIdHint => null;

        public IReadOnlyList<Profile> ListProfiles() => [.. Names.Keys.Select(id => GetById(id)!)];

        public Profile? GetById(string id)
            => Names.TryGetValue(id, out var name)
                ? new Profile(id, name, ProfileRole.User, DateTimeOffset.MinValue, DateTimeOffset.MinValue, false)
                : null;

        public void Rename(string id, string newDisplayName) => Names[id] = newDisplayName.Trim();

        public Profile Create(string displayName, ProfileRole role) => throw new NotSupportedException();
        public void Delete(string id, bool deleteData) => throw new NotSupportedException();
        public void SetActiveProfileHint(string id) => throw new NotSupportedException();
        public void SetPin(string id, string? pin) => throw new NotSupportedException();
        public bool VerifyPin(string id, string pin) => throw new NotSupportedException();
        public void SetRole(string id, ProfileRole role) => throw new NotSupportedException();
        public ProfilePinHash? GetPinHash(string id) => null;
        public bool HasPin(string id) => throw new NotSupportedException();
    }
}
