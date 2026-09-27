using FluentAssertions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

public class OperationCodecTests
{
    private static readonly Guid M = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");
    private static readonly Guid F = Guid.Parse("0f0f0f0f-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 9, 27, 10, 15, 30, TimeSpan.FromHours(2));

    public static TheoryData<SyncOperationBody> Samples() => new()
    {
        new MedicineCreated(M, new DateOnly(2026, 9, 1), At,
            [new MedicineFieldValue("Name", "Enalapril"), new MedicineFieldValue("Notes", null)]),
        new MedicineFieldChanged(M, "ThresholdDays", "9"),
        new MedicineActivityChanged(M, F, new DateOnly(2026, 9, 27), false, At),
        new ScheduleRowRecorded(M, F, new DateOnly(2026, 10, 1), 0.5m, 2, ScheduleKind.Weekly, "{\"days\":[1]}", At),
        new SlotSetRecorded(M, F, new DateOnly(2026, 9, 27), At,
            [new SlotValue(Guid.Parse("0a0a0a0a-0000-0000-0000-000000000003"), 1.5m, new TimeOnly(8, 30), "breakfast", 0)]),
        new StockEntryRecorded(M, F, StockMovementKind.NegativeCorrection, -2.25m, At, "broken"),
        new IntakeRecorded(M, F, new DateOnly(2026, 9, 26), IntakeStatus.Skipped, 1m, null, null, null, At),
        new StockCountRecorded(M, F, new DateOnly(2026, 9, 27), 40m, 1m, 7, At, "counted",
            42m, 2m, -1m, false, true),
        new SuspensionRecorded(M, F, new DateOnly(2026, 9, 28), null, "trip", At),
        new SuspensionEndChanged(M, F, new DateOnly(2026, 10, 3)),
        new FactRetracted(M, F, FactKind.StockCount, Guid.Parse("0c0c0c0c-0000-0000-0000-000000000004"), At),
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_operation_round_trips(SyncOperationBody body)
    {
        var (type, payload) = OperationCodec.Serialize(body);

        var back = OperationCodec.Deserialize(type, OperationCodec.CurrentSchemaVersion, payload);

        back.Should().BeOfType(body.GetType());
        OperationCodec.Serialize(back).Should().Be((type, payload));
        back.MedicineId.Should().Be(body.MedicineId);
        type.Should().Be(body.GetType().Name);
    }

    [Fact]
    public void Catalogue_covers_every_operation_type()
    {
        var concrete = typeof(SyncOperationBody).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(SyncOperationBody)) && !t.IsAbstract)
            .Select(t => t.Name);

        OperationCodec.TypeNames.Should().BeEquivalentTo(concrete);
        ((IEnumerable<object[]>)Samples()).Select(row => row[0].GetType().Name).Should().BeEquivalentTo(concrete);
    }

    // Pins the wire format: property names, enum names, date and decimal
    // forms. A change here is a sync format change (schema version).
    [Fact]
    public void Payload_format_is_stable()
    {
        var (_, payload) = OperationCodec.Serialize(
            new StockEntryRecorded(M, F, StockMovementKind.NewPackage, 28m, At, null));

        payload.Should().Be(
            "{\"movementId\":\"0f0f0f0f-0000-0000-0000-000000000002\"," +
            "\"kind\":\"NewPackage\",\"quantityDelta\":28," +
            "\"occurredAt\":\"2026-09-27T10:15:30+02:00\",\"notes\":null," +
            "\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"}");
    }

    [Fact]
    public void Unknown_type_or_newer_schema_is_not_supported()
    {
        var (type, payload) = OperationCodec.Serialize(new MedicineFieldChanged(M, "Name", "x"));

        FluentActions.Invoking(() => OperationCodec.Deserialize("DeviceRevoked", 1, "{}"))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => OperationCodec.Deserialize(type, OperationCodec.CurrentSchemaVersion + 1, payload))
            .Should().Throw<NotSupportedException>();
    }
}
