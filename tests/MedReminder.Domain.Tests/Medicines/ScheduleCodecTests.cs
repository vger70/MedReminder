using FluentAssertions;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Medicines;

public class ScheduleCodecTests
{
    [Fact]
    public void FixedDaily_has_null_payload_and_round_trips_through_legacy_fields()
    {
        var original = new FixedDailySchedule(1.5m, 3);
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.FixedDaily);
        payload.Should().BeNull();

        var restored = ScheduleCodec.Deserialize(
            kind,
            payload,
            legacyDosePerAdministration: 1.5m,
            legacyAdministrationsPerDay: 3);

        restored.Should().BeOfType<FixedDailySchedule>()
            .Which.Should().Be(original);
    }

    [Fact]
    public void Weekly_round_trips_via_json()
    {
        var original = new WeeklySchedule(new[] { 1m, 0.5m, 1m, 0.5m, 1m, 0m, 0m });
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.Weekly);
        payload.Should().NotBeNullOrWhiteSpace();

        var restored = ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        restored.Should().BeOfType<WeeklySchedule>()
            .Which.QuantitiesByDayOfWeek.Should().Equal(original.QuantitiesByDayOfWeek);
    }

    [Fact]
    public void Cyclic_round_trips_via_json()
    {
        var original = new CyclicSchedule(21, 7, 1m);
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.Cyclic);

        var restored = ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        restored.Should().Be(original);
    }

    [Fact]
    public void Tapering_round_trips_via_json()
    {
        var original = new TaperingSchedule(4m, 0.5m, 0.5m, 7);
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.Tapering);

        var restored = ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        restored.Should().Be(original);
    }

    [Fact]
    public void Prn_round_trips_with_empty_object_payload()
    {
        var original = new PrnSchedule();
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.Prn);
        payload.Should().Be("{}");

        var restored = ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        restored.Should().BeOfType<PrnSchedule>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stepped_tapering_round_trips_via_json(bool maintainLastDose)
    {
        var original = new SteppedTaperingSchedule(
            new[]
            {
                new TaperStage(4m, 7),
                new TaperStage(2m, 7),
                new TaperStage(1m, 14),
            },
            maintainLastDose);
        var (kind, payload) = ScheduleCodec.Serialize(original);
        kind.Should().Be(ScheduleKind.SteppedTapering);
        payload.Should().NotBeNullOrWhiteSpace();

        var restored = ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        restored.Should().Be(original);
    }

    [Fact]
    public void Stepped_tapering_payload_missing_stages_raises_invalid_operation()
    {
        var payload = "{\"maintainLastDose\":false}";
        Action act = () => ScheduleCodec.Deserialize(ScheduleKind.SteppedTapering, payload, 1m, 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Stepped_tapering_payload_with_a_single_stage_is_rejected()
    {
        // The constructor invariant (>= 2 stages) must fire on read too.
        var payload = "{\"maintainLastDose\":false,\"stages\":[{\"dose\":1,\"days\":7}]}";
        Action act = () => ScheduleCodec.Deserialize(ScheduleKind.SteppedTapering, payload, 1m, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unknown_enum_value_falls_back_to_fixed_daily()
    {
        // A future ScheduleKind value read by an older build must not
        // crash the projection — it falls back to FixedDaily and the
        // therapy rate keeps computing.
        var restored = ScheduleCodec.Deserialize(
            (ScheduleKind)9999,
            payload: null,
            legacyDosePerAdministration: 2m,
            legacyAdministrationsPerDay: 2);

        restored.Should().BeOfType<FixedDailySchedule>()
            .Which.Should().Be(new FixedDailySchedule(2m, 2));
    }

    [Theory]
    [InlineData(ScheduleKind.Weekly, "not json")]
    [InlineData(ScheduleKind.Cyclic, "{ malformed")]
    [InlineData(ScheduleKind.Tapering, "]")]
    public void Malformed_payload_raises_invalid_operation(ScheduleKind kind, string payload)
    {
        Action act = () => ScheduleCodec.Deserialize(kind, payload, 1m, 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Weekly_payload_with_wrong_length_is_rejected()
    {
        var payload = "{\"days\":[1,1,1]}";
        Action act = () => ScheduleCodec.Deserialize(ScheduleKind.Weekly, payload, 1m, 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Missing_payload_for_non_fixed_kind_raises_invalid_operation()
    {
        Action act = () => ScheduleCodec.Deserialize(ScheduleKind.Cyclic, null, 1m, 1);
        act.Should().Throw<InvalidOperationException>();
    }
}
