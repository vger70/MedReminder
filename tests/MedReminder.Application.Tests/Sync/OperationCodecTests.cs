using FluentAssertions;
using MedReminder.Application.Sync;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
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
        new SlotSetRecorded(M, F, new DateOnly(2026, 9, 27), At,
            [new SlotValue(Guid.Parse("0a0a0a0a-0000-0000-0000-000000000003"), 1.5m, new TimeOnly(8, 30), "breakfast", 0),
                new SlotValue(Guid.Parse("0a0a0a0a-0000-0000-0000-000000000005"), 1m, null, "as needed", 1,
                    IsAsNeeded: true)]),
        new IntakeRecorded(M, F, new DateOnly(2026, 9, 26), IntakeStatus.Skipped, 1m, null, null, null, At),
        new IntakeRecorded(M, F, new DateOnly(2026, 9, 26), IntakeStatus.Taken, 1m, null, At, null, At, IsExtra: true),
        new StockCountRecorded(M, F, new DateOnly(2026, 9, 27), 40m, 1m, 7, At, "counted",
            42m, 2m, -1m, false, true),
        new SuspensionRecorded(M, F, new DateOnly(2026, 9, 28), null, "trip", At),
        new SuspensionEndChanged(M, F, new DateOnly(2026, 10, 3)),
        new FactRetracted(M, F, FactKind.StockCount, Guid.Parse("0c0c0c0c-0000-0000-0000-000000000004"), At),
        new MedicineDeleted(M, At),
        new ProfileSettingChanged(ProfileSetting.CaregiverAddress, "carer@example.org"),
        new EmailNotificationSent(M, F, 3, Guid.Parse("0a0a0a0a-0000-0000-0000-000000000004"), At),
        new EmailNotificationSent(M, F, 3, null, At, Stage: 2),
        new HouseholdLinked(Guid.Parse("0d0d0d0d-0000-0000-0000-000000000005"), At),
        new PrescriptionChanged(M, F, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), "1234ABCD5678",
            2, new DateOnly(2026, 10, 21), null, false, At),
        new DeadlineChanged(M, F, DeadlineKind.TherapeuticPlan, "Plan AIFA", new DateOnly(2027, 3, 31), 30, 12,
            NotificationChannels.Both, null, false, At),
        new DeadlineChanged(Guid.Empty, F, DeadlineKind.Other, "Disability card", new DateOnly(2027, 1, 15), 0, null,
            NotificationChannels.Windows, new DateOnly(2027, 1, 10), false, At),
        new MedicineStartChanged(M, new DateOnly(2026, 9, 5)),
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
        ((IEnumerable<object[]>)Samples()).Select(row => row[0].GetType().Name).Distinct()
            .Should().BeEquivalentTo(concrete);
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

    // Only the type added in a version is written with it, so an older
    // app keeps reading every other operation.
    [Theory]
    [MemberData(nameof(Samples))]
    public void Operations_are_written_with_the_lowest_schema_version_that_carries_them(SyncOperationBody body)
    {
        var expected = body switch
        {
            MedicineDeleted => 2,
            ProfileSettingChanged => 3,
            EmailNotificationSent { Stage: > 1 } => 6,
            EmailNotificationSent => 4,
            HouseholdLinked => 5,
            PrescriptionChanged => 7,
            DeadlineChanged => 8,
            IntakeRecorded { IsExtra: true } => 9,
            SlotSetRecorded set when set.Slots.Any(s => s.IsAsNeeded) => 9,
            MedicineStartChanged => 10,
            _ => 1,
        };

        OperationCodec.SchemaVersionOf(body).Should().Be(expected);
        var (type, payload) = OperationCodec.Serialize(body);
        OperationCodec.Deserialize(type, expected, payload).Should().BeOfType(body.GetType());
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

    // Second low-stock warning: an EmailNotificationSent written before
    // the stage existed (schema version 4, no "stage") is a first-stage
    // email.
    [Fact]
    public void An_email_payload_without_a_stage_is_a_first_stage_email()
    {
        const string payload =
            "{\"notificationId\":\"0f0f0f0f-0000-0000-0000-000000000002\",\"stockEpoch\":3," +
            "\"epochFactId\":null,\"sentAt\":\"2026-09-27T10:15:30+02:00\"," +
            "\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"}";

        var back = OperationCodec.Deserialize("EmailNotificationSent", 4, payload);

        back.Should().BeOfType<EmailNotificationSent>().Which.Stage.Should().Be(1);
    }

    // As-needed doses (schema version 9): payloads written before the
    // flags read as a scheduled intake and a scheduled slot.
    [Fact]
    public void Payloads_without_the_as_needed_flags_read_false()
    {
        const string intake =
            "{\"intakeId\":\"0f0f0f0f-0000-0000-0000-000000000002\",\"day\":\"2026-09-26\"," +
            "\"status\":\"Taken\",\"quantity\":1,\"scheduledAt\":null,\"actualAt\":null,\"notes\":null," +
            "\"recordedAt\":\"2026-09-27T10:15:30+02:00\"," +
            "\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"}";
        const string set =
            "{\"setId\":\"0f0f0f0f-0000-0000-0000-000000000002\",\"effectiveFrom\":\"2026-09-27\"," +
            "\"recordedAt\":\"2026-09-27T10:15:30+02:00\",\"slots\":[{\"slotId\":" +
            "\"0a0a0a0a-0000-0000-0000-000000000003\",\"dose\":1,\"time\":null,\"timingLabel\":\"x\",\"order\":0}]," +
            "\"baseVersion\":null,\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"}";

        OperationCodec.Deserialize("IntakeRecorded", 1, intake)
            .Should().BeOfType<IntakeRecorded>().Which.IsExtra.Should().BeFalse();
        OperationCodec.Deserialize("SlotSetRecorded", 1, set)
            .Should().BeOfType<SlotSetRecorded>().Which.Slots.Single().IsAsNeeded.Should().BeFalse();
    }

    // A first-stage email keeps schema version 4 although its payload now
    // carries "stage": a reader ignores a property it does not know, as an
    // older app does with this field.
    [Fact]
    public void A_property_the_reader_does_not_know_is_ignored()
    {
        const string payload =
            "{\"field\":\"Name\",\"value\":\"x\",\"stage\":1," +
            "\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\"}";

        OperationCodec.Deserialize("MedicineFieldChanged", 1, payload)
            .Should().Be(new MedicineFieldChanged(M, "Name", "x"));
    }
}
