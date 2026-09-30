using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Household;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Household;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Household;

// Household step H2: the local household store on a real SQLite file.
public sealed class SqliteHouseholdStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-household-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_identity_is_created_once_and_kept()
    {
        var first = await new SqliteHouseholdStore(_directory).EnsureCreatedAsync(CancellationToken.None);
        var again = await new SqliteHouseholdStore(_directory).EnsureCreatedAsync(CancellationToken.None);

        again.Should().Be(first);
        first.Generation.Should().Be(1);
        File.Exists(Path.Combine(_directory, SqliteHouseholdStore.SettingsFileName)).Should().BeTrue();
        File.Exists(Path.Combine(_directory, SqliteHouseholdStore.DatabaseFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task Operations_and_registers_survive_a_new_instance()
    {
        var log = new HouseholdLog(new SqliteHouseholdStore(_directory), TimeProvider.System);
        await log.AppendAsync([
            new ProfileRegistered("default", "Anna", HouseholdRole.Admin, DateTimeOffset.UnixEpoch),
            new ProfilePinChanged("default", "h", "s", 100_000),
            new ProfileRoleChanged("default", HouseholdRole.User),
        ], CancellationToken.None);

        var reopened = new SqliteHouseholdStore(_directory);
        var profiles = await new HouseholdLog(reopened, TimeProvider.System).ProfilesAsync(CancellationToken.None);
        var operations = await reopened.ListOperationsAsync(CancellationToken.None);

        profiles.Should().ContainSingle().Which.Should().Be(
            new HouseholdProfile("default", "Anna", HouseholdRole.User, "100000:s:h"));
        operations.Select(o => o.Type).Should().Equal("ProfileRegistered", "ProfilePinChanged", "ProfileRoleChanged");
        operations.Select(o => o.Timestamp).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task The_latest_timestamp_breaks_ties_by_device()
    {
        var store = new SqliteHouseholdStore(_directory);
        var a = Guid.Parse("0000000000000000000000000000000a");
        var b = Guid.Parse("0000000000000000000000000000000b");
        foreach (var at in new[] { new HybridTimestamp(5, 1, b), new HybridTimestamp(5, 1, a), new HybridTimestamp(4, 9, b) })
        {
            await store.AppendAsync(new HouseholdOperationRecord(Guid.NewGuid(), at, 1, "ProfileRemoved", 1, "p",
                "{\"profileId\":\"p\"}"), [], CancellationToken.None);
        }

        (await store.GetLatestTimestampAsync(CancellationToken.None)).Should().Be(new HybridTimestamp(5, 1, b));
    }

    [Fact]
    public async Task An_empty_store_has_no_latest_timestamp_and_no_profiles()
    {
        var store = new SqliteHouseholdStore(_directory);

        (await store.GetLatestTimestampAsync(CancellationToken.None)).Should().BeNull();
        (await new HouseholdLog(store, TimeProvider.System).ProfilesAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
