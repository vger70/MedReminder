using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Which dose slots an intake answers, and the state of each slot of a
// day (mobile issues #126, #127).
public class DoseSlotsTests
{
    private static readonly DateOnly Day = new(2026, 9, 15);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly MedicationAdministrationSlot Morning = Slot(new TimeOnly(8, 0));
    private static readonly MedicationAdministrationSlot Evening = Slot(new TimeOnly(20, 0));
    private static readonly MedicationScheduleHistory[] Schedule = [DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 2)];

    [Fact]
    public void An_intake_scheduled_at_a_slot_covers_that_slot_only()
    {
        var intake = Intake(scheduledAt: At(Day, 8, 0));

        var records = DoseSlots.Records(Day, [Morning, Evening], [intake], Utc);

        records.Keys.Should().Equal(Morning.Id);
        records[Morning.Id].Should().BeSameAs(intake);
    }

    [Theory]
    [InlineData(IntakeStatus.Taken)]
    [InlineData(IntakeStatus.Skipped)]
    [InlineData(IntakeStatus.Cancelled)]
    public void Any_status_covers_the_slot(IntakeStatus status)
        => DoseSlots.Records(Day, [Morning], [Intake(At(Day, 8, 0), status)], Utc).Should().ContainKey(Morning.Id);

    [Fact]
    public void An_extra_dose_covers_no_slot()
        => DoseSlots.Records(Day, [Morning], [Intake(At(Day, 8, 0), isExtra: true)], Utc).Should().BeEmpty();

    [Fact]
    public void An_intake_of_another_day_covers_no_slot()
    {
        var yesterday = Day.AddDays(-1);
        var intake = Intake(At(yesterday, 8, 0), day: yesterday);

        DoseSlots.Records(Day, [Morning], [intake], Utc).Should().BeEmpty();
    }

    [Fact]
    public void Without_scheduled_at_an_intake_covers_the_single_timed_slot()
    {
        var asNeeded = Slot(new TimeOnly(12, 0), asNeeded: true);

        DoseSlots.Records(Day, [Morning, asNeeded], [Intake(scheduledAt: null)], Utc).Keys.Should().Equal(Morning.Id);
    }

    [Fact]
    public void Without_scheduled_at_an_intake_covers_none_of_several_slots()
        => DoseSlots.Records(Day, [Morning, Evening], [Intake(scheduledAt: null)], Utc).Should().BeEmpty();

    [Fact]
    public void A_scheduled_time_matching_no_slot_covers_none()
        => DoseSlots.Records(Day, [Morning, Evening], [Intake(At(Day, 9, 30))], Utc).Should().BeEmpty();

    // The single slot moved from 08:00 to 08:30 after its intake: that
    // intake is still the dose of the day.
    [Fact]
    public void A_scheduled_time_matching_no_slot_covers_the_single_timed_slot()
    {
        var moved = Slot(new TimeOnly(8, 30));

        DoseSlots.Records(Day, [moved], [Intake(At(Day, 8, 0))], Utc).Keys.Should().Equal(moved.Id);
    }

    [Fact]
    public void The_slot_is_read_in_the_zone_or_as_the_recorded_wall_clock()
    {
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        // 08:00 in Rome (UTC+2 in September), stored with its own offset
        // or as UTC.
        var withOffset = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.FromHours(2));

        DoseSlots.Records(Day, [Morning], [Intake(withOffset)], rome).Should().ContainKey(Morning.Id);
        DoseSlots.Records(Day, [Morning], [Intake(withOffset.ToUniversalTime())], rome).Should().ContainKey(Morning.Id);
    }

    // A device in another zone (Rome) recorded the 09:00 dose: the wall
    // clock it recorded wins over this device's zone (London), where the
    // same instant reads 08:00.
    [Fact]
    public void The_recorded_wall_clock_wins_over_this_zone()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var nine = Slot(new TimeOnly(9, 0));
        var intake = Intake(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(2)));

        DoseSlots.Records(Day, [Morning, Slot(new TimeOnly(8, 0)), nine], [intake], london).Keys.Should().Equal(nine.Id);
    }

    [Fact]
    public void The_first_recorded_intake_answers_the_slot()
    {
        var first = Intake(At(Day, 8, 0), recordedAt: At(Day, 8, 5));
        var second = Intake(At(Day, 8, 0), IntakeStatus.Skipped, recordedAt: At(Day, 8, 10));

        DoseSlots.Records(Day, [Morning], [second, first], Utc)[Morning.Id].Should().BeSameAs(first);
    }

    [Fact]
    public void States_follow_the_time_and_the_records()
    {
        var now = At(Day, 20, 10);
        var noon = Slot(new TimeOnly(12, 0));
        var night = Slot(new TimeOnly(23, 0));

        var states = DoseSlots.StatesOn(Day, now, Utc, DomainFactory.Medicine(), Schedule, [Evening, night, Morning, noon],
            [], stockAtStartOfDay: 10m, [Intake(At(Day, 8, 0))], TimeSpan.FromMinutes(30));

        states.Select(s => (s.Slot.Id, s.State)).Should().Equal(
            (Morning.Id, DoseSlotState.Recorded),
            (noon.Id, DoseSlotState.NotRecorded),
            (Evening.Id, DoseSlotState.Due),
            (night.Id, DoseSlotState.Upcoming));
        states[0].Intake.Should().NotBeNull();
        states[2].At.Should().Be(At(Day, 20, 0));
    }

    [Fact]
    public void No_states_on_a_day_with_no_dose_due()
    {
        var medicine = DomainFactory.Medicine(startDate: Day.AddDays(1));
        var now = At(Day, 9, 0);
        var window = TimeSpan.FromMinutes(30);

        DoseSlots.StatesOn(Day, now, Utc, medicine, Schedule, [Morning], [], 10m, [], window).Should().BeEmpty();
        DoseSlots.StatesOn(Day, now, Utc, DomainFactory.Medicine(), Schedule, [Morning], [], 0m, [], window).Should().BeEmpty();
        DoseSlots.StatesOn(Day, now, Utc, DomainFactory.Medicine(), Schedule, [Morning],
            [DomainFactory.Suspension(Day)], 10m, [], window).Should().BeEmpty();
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minute)
        => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static MedicationAdministrationSlot Slot(TimeOnly time, bool asNeeded = false) => new()
    {
        MedicineId = DomainFactory.MedicineId,
        Dose = 1m,
        Time = time,
        IsAsNeeded = asNeeded,
    };

    private static MedicationIntake Intake(
        DateTimeOffset? scheduledAt,
        IntakeStatus status = IntakeStatus.Taken,
        bool isExtra = false,
        DateOnly? day = null,
        DateTimeOffset? recordedAt = null) => new()
    {
        MedicineId = DomainFactory.MedicineId,
        Day = day ?? Day,
        ScheduledAt = scheduledAt,
        Quantity = 1m,
        Status = status,
        IsExtra = isExtra,
        RecordedAt = recordedAt ?? At(Day, 7, 0),
    };
}
