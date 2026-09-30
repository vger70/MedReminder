using FluentAssertions;
using MedReminder.Application.Household;
using MedReminder.Domain.Household;
using Xunit;

namespace MedReminder.Application.Tests.Household;

public class HouseholdOperationCodecTests
{
    public static TheoryData<HouseholdOperationBody> Samples() => new()
    {
        new ProfileRegistered("0a0a0a0a000000000000000000000001", "Anna", HouseholdRole.Admin,
            new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(2))),
        new ProfileRenamed("default", "Anna B."),
        new ProfileRoleChanged("default", HouseholdRole.User),
        new ProfilePinChanged("default", "aGFzaA==", "c2FsdA==", 100_000),
        new ProfilePinChanged("default", null, null, 0),
        new ProfileRemoved("default"),
        new HouseholdSettingChanged(HouseholdSetting.SmtpHost, "smtp.example.org"),
        new HouseholdSettingChanged(HouseholdSetting.SmtpPassword, null),
        new DeviceKeyPublished(Guid.Parse("0a0a0a0a000000000000000000000002"), "cHVibGlj"),
        new RecoveryKeyPublished(1, "cmVjb3Zlcnk="),
        new ProfileKeyGranted("default", Guid.Parse("0a0a0a0a000000000000000000000002"),
            Guid.Parse("0a0a0a0a000000000000000000000003"), 1, "1.a.b.c.d"),
        new ProfileKeyRevoked("default", Guid.Parse("0a0a0a0a000000000000000000000002")),
        new ProfileKeyEscrowed("default", Guid.Parse("0a0a0a0a000000000000000000000003"), 1, "1.a.b.c.d"),
        new MasterElected(Guid.Parse("0e0e0e0e000000000000000000000001"), Guid.Parse("0a0a0a0a000000000000000000000002"),
            "default", MasterElectionKind.Planned),
        new MasterActivated(Guid.Parse("0e0e0e0e000000000000000000000001"), Guid.Parse("0a0a0a0a000000000000000000000002")),
        new MasterReleased(Guid.Parse("0e0e0e0e000000000000000000000001")),
        new DeviceRemoved(Guid.Parse("0a0a0a0a000000000000000000000002")),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_operation_round_trips(HouseholdOperationBody body)
    {
        var (type, payload) = HouseholdOperationCodec.Serialize(body);

        HouseholdOperationCodec.Deserialize(type, HouseholdOperationCodec.SchemaVersionOf(body), payload).Should().Be(body);
    }

    [Fact]
    public void Catalogue_covers_every_operation_type()
    {
        var concrete = typeof(HouseholdOperationBody).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(HouseholdOperationBody)) && !t.IsAbstract)
            .Select(t => t.Name);

        HouseholdOperationCodec.TypeNames.Should().BeEquivalentTo(concrete);
    }

    // Pins the wire format.
    [Fact]
    public void Payload_format_is_stable()
    {
        HouseholdOperationCodec.Serialize(new ProfileRoleChanged("default", HouseholdRole.Admin))
            .Should().Be(("ProfileRoleChanged", "{\"role\":\"admin\",\"profileId\":\"default\"}"));
    }

    [Fact]
    public void Unknown_type_or_newer_schema_is_not_supported()
    {
        FluentActions.Invoking(() => HouseholdOperationCodec.Deserialize("SomethingNew", 1, "{}"))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => HouseholdOperationCodec.Deserialize("ProfileRemoved", 2, "{\"profileId\":\"x\"}"))
            .Should().Throw<NotSupportedException>();
    }
}
