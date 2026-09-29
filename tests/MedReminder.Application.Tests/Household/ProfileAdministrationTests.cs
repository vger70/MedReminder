using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Household;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Household;

// Household step H2: profile administration through use cases that
// write profiles.json and record the change in the household.
public class ProfileAdministrationTests
{
    private readonly InMemoryProfileRegistry _registry = new();
    private readonly InMemoryHouseholdStore _store = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));

    public ProfileAdministrationTests()
    {
        _registry.Add("admin", "Anna", ProfileRole.Admin);
        _registry.Add("user", "Bruno", ProfileRole.User);
    }

    private HouseholdLog Log => new(_store, _clock);

    private static ICurrentProfile As(string id, ProfileRole role) => new FixedCurrentProfile(id, role);

    private async Task<IReadOnlyList<HouseholdProfile>> HouseholdAsync() => await Log.ProfilesAsync(CancellationToken.None);

    private Task ReconcileAsync() => new ReconcileHousehold(_registry, Log).ExecuteAsync(CancellationToken.None);

    [Fact]
    public async Task Reconcile_records_every_profile_once()
    {
        _registry.SetPin("user", "1234");

        (await new ReconcileHousehold(_registry, Log).ExecuteAsync(CancellationToken.None)).Should().Be(3);
        (await new ReconcileHousehold(_registry, Log).ExecuteAsync(CancellationToken.None)).Should().Be(0);

        var profiles = await HouseholdAsync();
        profiles.Should().BeEquivalentTo([
            new HouseholdProfile("admin", "Anna", HouseholdRole.Admin, null),
            new HouseholdProfile("user", "Bruno", HouseholdRole.User, "100000:salt:hash-of-1234"),
        ]);
    }

    [Fact]
    public async Task Reconcile_records_changes_made_outside_the_use_cases_but_never_a_removal()
    {
        await ReconcileAsync();
        _registry.Rename("user", "Bruna");
        _registry.SetRole("user", ProfileRole.Admin);
        _registry.Delete("admin", deleteData: false);

        (await new ReconcileHousehold(_registry, Log).ExecuteAsync(CancellationToken.None)).Should().Be(2);

        var profiles = await HouseholdAsync();
        profiles.Should().HaveCount(2, "a profile missing from profiles.json is not removed from the household");
        profiles.Single(p => p.ProfileId == "user").Should().Be(new HouseholdProfile("user", "Bruna", HouseholdRole.Admin, null));
    }

    [Fact]
    public async Task An_admin_changes_the_role_of_another_profile()
    {
        await ReconcileAsync();
        var changed = await new ChangeProfileRole(_registry, As("admin", ProfileRole.Admin), Log,
            NullLogger<ChangeProfileRole>.Instance).ExecuteAsync("user", ProfileRole.Admin, CancellationToken.None);

        changed.Should().BeTrue();
        _registry.GetById("user")!.Role.Should().Be(ProfileRole.Admin);
        (await HouseholdAsync()).Single(p => p.ProfileId == "user").Role.Should().Be(HouseholdRole.Admin);
        _store.Operations.Last().Type.Should().Be("ProfileRoleChanged");
    }

    [Fact]
    public async Task The_same_role_writes_nothing()
    {
        var before = _store.Operations.Count;

        var changed = await new ChangeProfileRole(_registry, As("admin", ProfileRole.Admin), Log,
            NullLogger<ChangeProfileRole>.Instance).ExecuteAsync("user", ProfileRole.User, CancellationToken.None);

        changed.Should().BeFalse();
        _store.Operations.Should().HaveCount(before);
    }

    [Theory]
    [InlineData("user", ProfileRole.User, "admin", ProfileRole.User, ProfileAdministrationError.NotAdmin)]
    [InlineData("admin", ProfileRole.Admin, "admin", ProfileRole.User, ProfileAdministrationError.ActiveProfile)]
    [InlineData("admin", ProfileRole.Admin, "nobody", ProfileRole.Admin, ProfileAdministrationError.NotFound)]
    public async Task A_role_change_is_refused(string currentId, ProfileRole currentRole, string target,
        ProfileRole role, ProfileAdministrationError expected)
    {
        var act = () => new ChangeProfileRole(_registry, As(currentId, currentRole), Log,
            NullLogger<ChangeProfileRole>.Instance).ExecuteAsync(target, role, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileAdministrationException>()).Which.Error.Should().Be(expected);
        _store.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_last_admin_cannot_be_demoted()
    {
        // With the open profile kept admin (D1), the last admin can only be
        // reached when the open profile is missing from profiles.json
        // (edited by hand while the app runs): the check still holds.
        _registry.Add("admin2", "Carla", ProfileRole.Admin);
        var useCase = new ChangeProfileRole(_registry, As("admin2", ProfileRole.Admin), Log,
            NullLogger<ChangeProfileRole>.Instance);
        (await useCase.ExecuteAsync("admin", ProfileRole.User, CancellationToken.None)).Should().BeTrue();

        var act = () => new ChangeProfileRole(_registry, As("ghost", ProfileRole.Admin), Log,
            NullLogger<ChangeProfileRole>.Instance).ExecuteAsync("admin2", ProfileRole.User, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileAdministrationException>()).Which.Error
            .Should().Be(ProfileAdministrationError.LastAdmin);
        _registry.GetById("admin2")!.Role.Should().Be(ProfileRole.Admin);
    }

    [Fact]
    public async Task Creating_a_profile_records_it_with_its_pin()
    {
        var created = await new CreateProfile(_registry, As("admin", ProfileRole.Admin), Log)
            .ExecuteAsync("Dario", ProfileRole.User, "9876", CancellationToken.None);

        (await HouseholdAsync()).Should().ContainEquivalentOf(
            new HouseholdProfile(created.Id, "Dario", HouseholdRole.User, "100000:salt:hash-of-9876"));
    }

    [Fact]
    public async Task A_user_cannot_create_a_profile()
    {
        var act = () => new CreateProfile(_registry, As("user", ProfileRole.User), Log)
            .ExecuteAsync("Dario", ProfileRole.User, null, CancellationToken.None);

        await act.Should().ThrowAsync<ProfileAdministrationException>();
        _registry.ListProfiles().Should().HaveCount(2);
    }

    [Fact]
    public async Task Deleting_a_profile_removes_it_from_the_household_for_good()
    {
        await ReconcileAsync();

        await new DeleteProfile(_registry, As("admin", ProfileRole.Admin), Log)
            .ExecuteAsync("user", deleteData: false, CancellationToken.None);
        _registry.Add("user", "Bruno", ProfileRole.User);
        await ReconcileAsync();

        (await HouseholdAsync()).Should().ContainSingle().Which.ProfileId.Should().Be("admin",
            "a removed profile never comes back, even with the same id");
    }

    [Fact]
    public async Task The_open_profile_cannot_be_deleted()
    {
        var act = () => new DeleteProfile(_registry, As("admin", ProfileRole.Admin), Log)
            .ExecuteAsync("admin", deleteData: false, CancellationToken.None);

        (await act.Should().ThrowAsync<ProfileAdministrationException>()).Which.Error
            .Should().Be(ProfileAdministrationError.ActiveProfile);
    }

    [Fact]
    public async Task A_user_sets_their_own_pin_but_not_another_one()
    {
        await new SetProfilePin(_registry, As("user", ProfileRole.User), Log)
            .ExecuteAsync("user", "1111", CancellationToken.None);
        var other = () => new SetProfilePin(_registry, As("user", ProfileRole.User), Log)
            .ExecuteAsync("admin", "2222", CancellationToken.None);

        await other.Should().ThrowAsync<ProfileAdministrationException>();
        var ops = await _store.ListOperationsAsync(CancellationToken.None);
        ops.Should().ContainSingle();
        HouseholdOperationCodec.Deserialize(ops[0].Type, ops[0].SchemaVersion, ops[0].Payload)
            .Should().Be(new ProfilePinChanged("user", "hash-of-1111", "salt", 100_000));
    }

    [Fact]
    public async Task Clearing_a_pin_records_no_hash()
    {
        _registry.SetPin("user", "1111");
        await ReconcileAsync();

        await new SetProfilePin(_registry, As("admin", ProfileRole.Admin), Log)
            .ExecuteAsync("user", null, CancellationToken.None);

        (await HouseholdAsync()).Single(p => p.ProfileId == "user").Pin.Should().BeNull();
    }

    [Fact]
    public async Task Timestamps_of_the_log_always_increase()
    {
        await ReconcileAsync();
        await new SetProfilePin(_registry, As("admin", ProfileRole.Admin), Log)
            .ExecuteAsync("user", "1", CancellationToken.None);

        var timestamps = _store.Operations.Select(o => o.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }
}
