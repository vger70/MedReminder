using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Application.Tests.Support;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Household;

// Household step H3c: an adopted profile group records the household that
// claims it (HouseholdLinked), and the earliest claim wins.
public class HouseholdLinksTests
{
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, 500, TimeSpan.Zero));
    private readonly InMemoryHouseholdStore _householdA = new();
    private readonly InMemoryHouseholdStore _householdB = new();
    private readonly Guid _group;

    public HouseholdLinksTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _group = group.GroupId;
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
    }

    private HouseholdLinks Links(ApplicationTestScope scope, InMemoryHouseholdStore store)
        => new(store, new HouseholdLog(store, scope.Clock), new FixedCurrentProfile("p", ProfileRole.Admin),
            scope.SyncSettingsStore, scope.SyncOperations, scope.Operations, scope.Uow, scope.Clock);

    private static async Task<Guid> PublishAsync(ApplicationTestScope scope, InMemoryHouseholdStore store, Guid group)
    {
        var identity = await store.EnsureCreatedAsync(default);
        await store.SaveIdentityAsync(identity with { Storage = SyncTarget.ForFolder("remote") }, default);
        await new HouseholdLog(store, scope.Clock).AppendAsync(
            [new ProfileKeyEscrowed("p", group, 1, "wrapped")], default);
        return identity.HouseholdId;
    }

    [Fact]
    public async Task An_adopted_group_records_its_household_once()
    {
        (await Links(_a, _householdA).RecordAsync(default)).Should().BeFalse("the household is not published");

        var household = await PublishAsync(_a, _householdA, _group);

        (await Links(_a, _householdA).RecordAsync(default)).Should().BeTrue();
        (await Links(_a, _householdA).RecordAsync(default)).Should().BeFalse();
        (await _a.SyncOperations.ListOfTypeAsync("HouseholdLinked", default)).Should().ContainSingle()
            .Which.SchemaVersion.Should().Be(5);
        (await Links(_a, _householdA).StatusAsync(default))
            .Should().Be(new HouseholdLinkStatus(HouseholdLinkState.ThisHousehold, household));
    }

    [Fact]
    public async Task A_group_the_household_does_not_hold_is_not_claimed()
    {
        await PublishAsync(_a, _householdA, Guid.NewGuid());

        (await Links(_a, _householdA).RecordAsync(default)).Should().BeFalse();
        (await Links(_a, _householdA).StatusAsync(default)).State.Should().Be(HouseholdLinkState.None);
    }

    [Fact]
    public async Task The_earliest_claim_wins_on_every_device()
    {
        var first = await PublishAsync(_a, _householdA, _group);
        await PublishAsync(_b, _householdB, _group);
        await Links(_a, _householdA).RecordAsync(default);
        await Links(_b, _householdB).RecordAsync(default);

        await _b.ApplyRemote.ExecuteAsync(await _a.SyncOperations.ListAllAsync(default), default);
        await _a.ApplyRemote.ExecuteAsync(await _b.SyncOperations.ListAllAsync(default), default);

        (await Links(_a, _householdA).StatusAsync(default))
            .Should().Be(new HouseholdLinkStatus(HouseholdLinkState.ThisHousehold, first));
        (await Links(_b, _householdB).StatusAsync(default))
            .Should().Be(new HouseholdLinkStatus(HouseholdLinkState.OtherHousehold, first));
    }
}
