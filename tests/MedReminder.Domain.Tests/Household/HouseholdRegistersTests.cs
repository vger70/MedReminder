using FluentAssertions;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Domain.Tests.Household;

public class HouseholdRegistersTests
{
    private static readonly Guid DeviceA = Guid.Parse("0000000000000000000000000000000a");
    private static readonly Guid DeviceB = Guid.Parse("0000000000000000000000000000000b");

    private static IEnumerable<(string, string, HybridTimestamp, string?)> Versions(
        params (HouseholdOperationBody Body, HybridTimestamp At)[] operations)
        => operations.SelectMany(o => HouseholdRegisters.WritesOf(o.Body)
            .Select(w => (o.Body.ProfileId, w.Register, o.At, w.Value)));

    [Fact]
    public void The_latest_version_of_each_register_wins_whatever_the_order()
    {
        var created = (new ProfileRegistered("p", "Anna", HouseholdRole.User, DateTimeOffset.UnixEpoch) as HouseholdOperationBody,
            new HybridTimestamp(1, 0, DeviceA));
        var promoted = (new ProfileRoleChanged("p", HouseholdRole.Admin) as HouseholdOperationBody, new HybridTimestamp(5, 0, DeviceB));
        var demoted = (new ProfileRoleChanged("p", HouseholdRole.User) as HouseholdOperationBody, new HybridTimestamp(5, 0, DeviceA));

        var forward = HouseholdRegisters.Profiles(Versions(created, promoted, demoted));
        var backward = HouseholdRegisters.Profiles(Versions(demoted, promoted, created));

        // Same physical time and counter: the device id breaks the tie (b > a).
        forward.Should().ContainSingle().Which.Role.Should().Be(HouseholdRole.Admin);
        backward.Should().BeEquivalentTo(forward);
    }

    [Fact]
    public void A_removed_profile_stays_removed()
    {
        var profiles = HouseholdRegisters.Profiles(Versions(
            (new ProfileRegistered("p", "Anna", HouseholdRole.User, DateTimeOffset.UnixEpoch), new HybridTimestamp(1, 0, DeviceA)),
            (new ProfileRemoved("p"), new HybridTimestamp(2, 0, DeviceA)),
            (new ProfileRenamed("p", "Anna B."), new HybridTimestamp(3, 0, DeviceB))));

        profiles.Should().BeEmpty();
    }

    [Fact]
    public void A_register_of_an_unregistered_profile_is_not_a_profile()
    {
        HouseholdRegisters.Profiles(Versions((new ProfileRenamed("p", "Anna"), new HybridTimestamp(1, 0, DeviceA))))
            .Should().BeEmpty();
    }

    [Fact]
    public void Installation_settings_are_not_a_profile_and_the_latest_value_wins()
    {
        var versions = Versions(
            (new HouseholdSettingChanged(HouseholdSetting.SmtpHost, "old.example.org"), new HybridTimestamp(1, 0, DeviceA)),
            (new HouseholdSettingChanged(HouseholdSetting.SmtpHost, "new.example.org"), new HybridTimestamp(2, 0, DeviceB)),
            (new HouseholdSettingChanged(HouseholdSetting.SmtpPassword, null), new HybridTimestamp(3, 0, DeviceA))).ToList();

        HouseholdRegisters.Profiles(versions).Should().BeEmpty();
        HouseholdRegisters.Settings(versions).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            [HouseholdSetting.SmtpHost] = "new.example.org",
            [HouseholdSetting.SmtpPassword] = null,
        });
    }

    [Fact]
    public void Keys_hold_device_keys_grants_escrows_and_revocations()
    {
        var group = Guid.Parse("0000000000000000000000000000000c");
        var versions = Versions(
            (new DeviceKeyPublished(DeviceA, "keyA"), new HybridTimestamp(1, 0, DeviceA)),
            (new RecoveryKeyPublished(1, "recovery"), new HybridTimestamp(1, 1, DeviceA)),
            (new ProfileKeyGranted("p", DeviceA, group, 3, "1.w.a"), new HybridTimestamp(2, 0, DeviceA)),
            (new ProfileKeyGranted("p", DeviceB, group, 3, "1.w.b"), new HybridTimestamp(2, 1, DeviceA)),
            (new ProfileKeyRevoked("p", DeviceB), new HybridTimestamp(3, 0, DeviceA)),
            (new ProfileKeyEscrowed("p", group, 3, "1.w.e"), new HybridTimestamp(2, 2, DeviceA))).ToList();

        var keys = HouseholdRegisters.Keys(versions);

        keys.DevicePublicKeys.Should().Equal(new Dictionary<Guid, string> { [DeviceA] = "keyA" });
        keys.RecoveryPublicKey.Should().Be(new HouseholdWrappedKey(Guid.Empty, 1, "recovery"));
        keys.Grants.Keys.Should().Equal(("p", DeviceA));
        keys.Grants[("p", DeviceA)].Should().Be(new HouseholdWrappedKey(group, 3, "1.w.a"));
        keys.Escrows["p"].Should().Be(new HouseholdWrappedKey(group, 3, "1.w.e"));
        HouseholdRegisters.Profiles(versions).Should().BeEmpty();
        HouseholdRegisters.Settings(versions).Should().BeEmpty();
    }

    [Fact]
    public void A_pin_value_needs_hash_salt_and_iterations()
    {
        HouseholdRegisters.PinValue("h", "s", 100_000).Should().Be("100000:s:h");
        HouseholdRegisters.PinValue(null, "s", 100_000).Should().BeNull();
        HouseholdRegisters.PinValue("h", "s", 0).Should().BeNull();
    }
}
