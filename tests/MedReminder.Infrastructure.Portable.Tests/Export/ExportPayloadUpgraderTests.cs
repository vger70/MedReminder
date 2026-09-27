using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using MedReminder.Infrastructure.Tests.Support;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// Schema version 1 -> 2 (docs/EXPORT-FORMAT.md §5): an older archive is
// mapped like a database the Phase 2b boot patch meets for the first
// time.
public class ExportPayloadUpgraderTests
{
    private static readonly DateTimeOffset ImportInstant = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Version_1_payload_gets_slot_sets_and_a_cutoff()
    {
        var payload = VersionOnePayloadWithSlots(out var medicineId);

        ExportPayloadUpgrader.UpgradeToCurrent(payload, new TestTime(ImportInstant));

        payload.SchemaVersion.Should().Be(ExportFormat.CurrentSchemaVersion);
        var set = payload.MedicationAdministrationSlotSets.Should().ContainSingle().Subject;
        set.Id.Should().Be(medicineId);
        set.MedicineId.Should().Be(medicineId);
        set.EffectiveFrom.Should().Be(new DateOnly(2026, 9, 1));
        set.RecordedAt.Should().Be(ImportInstant);
        payload.MedicationAdministrationSlots.Should().OnlyContain(s => s.SetId == medicineId);
        payload.LedgerCutoff!.CutoffDay.Should().Be(new DateOnly(2026, 9, 24));
        payload.LedgerCutoff.FrozenAt.Should().Be(ImportInstant);
    }

    [Fact]
    public void Current_payload_is_left_as_is()
    {
        var payload = TestArchiveWriter.SamplePayload();

        ExportPayloadUpgrader.UpgradeToCurrent(payload, new TestTime(ImportInstant));

        payload.LedgerCutoff.Should().BeNull();
        payload.MedicationAdministrationSlotSets.Should().BeEmpty();
    }

    internal static ExportPayload VersionOnePayloadWithSlots(out Guid medicineId)
    {
        var payload = TestArchiveWriter.SamplePayload();
        payload.SchemaVersion = 1;
        medicineId = payload.Medicines.Single().Id;
        payload.MedicationAdministrationSlots.Add(new ExportedAdministrationSlot
        {
            Id = Guid.NewGuid(), MedicineId = medicineId, Dose = 1m, Time = new TimeOnly(8, 0), Order = 0,
        });
        payload.MedicationAdministrationSlots.Add(new ExportedAdministrationSlot
        {
            Id = Guid.NewGuid(), MedicineId = medicineId, Dose = 1m, Time = new TimeOnly(20, 0), Order = 1,
        });
        return payload;
    }

    internal sealed class TestTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTime(DateTimeOffset now) => _now = now.ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
