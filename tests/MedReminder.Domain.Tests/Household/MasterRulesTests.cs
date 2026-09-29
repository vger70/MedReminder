using FluentAssertions;
using MedReminder.Domain.Household;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Domain.Tests.Household;

// Household step H4a: the master registers and the rules of §7.4.
public class MasterRulesTests
{
    private static readonly Guid A = Guid.Parse("0a000000-0000-0000-0000-000000000000");
    private static readonly Guid B = Guid.Parse("0b000000-0000-0000-0000-000000000000");
    private static readonly Guid E1 = Guid.Parse("e1000000-0000-0000-0000-000000000000");
    private static readonly Guid E2 = Guid.Parse("e2000000-0000-0000-0000-000000000000");
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = MasterRules.DefaultLease;
    private static readonly TimeSpan Margin = MasterRules.DefaultMargin;

    private static HouseholdMaster State(params (HouseholdOperationBody Body, long Ms)[] operations)
        => HouseholdRegisters.Master(operations.SelectMany(o => HouseholdRegisters.WritesOf(o.Body)
            .Select(w => (o.Body.ProfileId, w.Register, new HybridTimestamp(o.Ms, 0, A), w.Value))));

    private static HouseholdMaster AActive() => State(
        (new MasterElected(E1, A, "admin", MasterElectionKind.Creation), 1),
        (new MasterActivated(E1, A), 2));

    private static HouseholdMaster BElected() => State(
        (new MasterElected(E1, A, "admin", MasterElectionKind.Creation), 1),
        (new MasterActivated(E1, A), 2),
        (new MasterElected(E2, B, "admin", MasterElectionKind.Planned), 3));

    [Fact]
    public void Without_an_election_every_device_sends()
        => MasterRules.SendsEmail(A, HouseholdMaster.None, published: true, lastSynced: null, Now, Lease).Should().BeTrue();

    [Fact]
    public void Only_the_active_master_sends_within_its_lease()
    {
        var master = AActive();

        master.ActiveDevice.Should().Be(A);
        MasterRules.SendsEmail(A, master, published: false, null, Now, Lease).Should().BeTrue();
        MasterRules.SendsEmail(A, master, published: true, Now.AddHours(-23), Now, Lease).Should().BeTrue();
        MasterRules.SendsEmail(A, master, published: true, Now.AddHours(-25), Now, Lease).Should().BeFalse("the lease ran out");
        MasterRules.SendsEmail(B, master, published: true, Now, Now, Lease).Should().BeFalse();
    }

    [Fact]
    public void Between_election_and_activation_no_device_sends()
    {
        var master = BElected();

        master.Pending.Should().BeTrue();
        master.OutgoingDevice.Should().Be(A);
        MasterRules.SendsEmail(A, master, true, Now, Now, Lease).Should().BeFalse();
        MasterRules.SendsEmail(B, master, true, Now, Now, Lease).Should().BeFalse();
    }

    [Fact]
    public void The_outgoing_master_releases_and_the_elected_device_then_activates()
    {
        var master = BElected();

        MasterRules.ShouldRelease(A, master).Should().BeTrue();
        MasterRules.ShouldRelease(B, master).Should().BeFalse();
        MasterRules.ShouldActivate(B, master, outgoingLastSeen: Now, Now, Lease, Margin).Should().BeFalse("A is alive and has not released");
        MasterRules.ShouldActivate(A, master, Now, Now, Lease, Margin).Should().BeFalse("not elected");

        var released = State(
            (new MasterElected(E1, A, "admin", MasterElectionKind.Creation), 1),
            (new MasterActivated(E1, A), 2),
            (new MasterElected(E2, B, "admin", MasterElectionKind.Planned), 3),
            (new MasterReleased(E2), 4));
        MasterRules.ShouldRelease(A, released).Should().BeFalse("already released");
        MasterRules.ShouldActivate(B, released, Now, Now, Lease, Margin).Should().BeTrue();

        var activated = State(
            (new MasterElected(E1, A, "admin", MasterElectionKind.Creation), 1),
            (new MasterActivated(E1, A), 2),
            (new MasterElected(E2, B, "admin", MasterElectionKind.Planned), 3),
            (new MasterReleased(E2), 4),
            (new MasterActivated(E2, B), 5));
        activated.ActiveDevice.Should().Be(B);
        activated.Pending.Should().BeFalse();
        MasterRules.ShouldActivate(B, activated, Now, Now, Lease, Margin).Should().BeFalse();
    }

    [Fact]
    public void An_outgoing_master_not_seen_for_the_lease_and_the_margin_is_taken_over()
    {
        var master = BElected();

        MasterRules.ShouldActivate(B, master, Now - Lease - Margin + TimeSpan.FromMinutes(1), Now, Lease, Margin)
            .Should().BeFalse();
        MasterRules.ShouldActivate(B, master, Now - Lease - Margin - TimeSpan.FromMinutes(1), Now, Lease, Margin)
            .Should().BeTrue();
        MasterRules.ShouldActivate(B, master, outgoingLastSeen: null, Now, Lease, Margin).Should().BeTrue("no record");
    }

    [Fact]
    public void A_first_election_activates_at_once_and_the_last_election_wins()
    {
        var first = State((new MasterElected(E1, A, "admin", MasterElectionKind.Creation), 1));
        MasterRules.ShouldActivate(A, first, null, Now, Lease, Margin).Should().BeTrue();

        var concurrent = State(
            (new MasterElected(E2, B, "admin", MasterElectionKind.Planned), 5),
            (new MasterElected(E1, A, "admin", MasterElectionKind.Planned), 3));
        concurrent.Election!.DeviceId.Should().Be(B);
    }
}
