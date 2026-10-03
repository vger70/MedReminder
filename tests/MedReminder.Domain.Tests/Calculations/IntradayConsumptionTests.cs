using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Today's doses already due (docs/analysis/ANALYSIS-INTRADAY-CONSUMPTION.md
// §4): what the main list subtracts from the start-of-day stock.
public class IntradayConsumptionTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);
    private static readonly Guid Morning = BuiltInDoseTimePresets.IdOf("Morning");
    private static readonly Guid Unknown = Guid.Parse("99999999-0000-0000-0000-000000000001");

    private static readonly MedicationScheduleHistory[] TwiceDaily =
        [DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 2)];

    private static MedicationAdministrationSlot Slot(
        decimal dose, TimeOnly? time = null, Guid? preset = null, bool asNeeded = false)
        => new() { MedicineId = DomainFactory.MedicineId, Dose = dose, Time = time, PresetId = preset, IsAsNeeded = asNeeded };

    private static decimal Due(
        TimeOnly now,
        IReadOnlyList<MedicationAdministrationSlot>? slots = null,
        IEnumerable<MedicationScheduleHistory>? schedule = null,
        IEnumerable<MedicationSuspension>? suspensions = null,
        Medicine? medicine = null,
        bool booked = false)
        => IntradayConsumption.DueSoFar(
            medicine ?? DomainFactory.Medicine(),
            Today, now,
            schedule ?? TwiceDaily,
            suspensions ?? [],
            slots ?? [],
            booked,
            id => id == Morning ? new TimeOnly(8, 0) : null,
            DoseTimeDefault.BuiltIn);

    [Theory]
    [InlineData(7, 59, 0)]
    [InlineData(8, 0, 1)]
    [InlineData(19, 59, 1)]
    [InlineData(20, 0, 3)]
    public void Timed_slots_are_due_from_their_time(int hour, int minute, int expected)
    {
        var slots = new[] { Slot(1m, new TimeOnly(8, 0)), Slot(2m, new TimeOnly(20, 0)) };

        Due(new TimeOnly(hour, minute), slots).Should().Be(expected);
    }

    [Fact]
    public void A_slot_without_time_uses_its_preset_time()
    {
        var slots = new[] { Slot(1m, preset: Morning), Slot(1m, new TimeOnly(20, 0)) };

        Due(new TimeOnly(9, 0), slots).Should().Be(1m);
    }

    [Fact]
    public void A_slot_without_a_resolvable_time_waits_for_the_end_of_day()
    {
        var slots = new[] { Slot(1m, preset: Unknown), Slot(1m), Slot(1m, new TimeOnly(6, 0)) };

        Due(new TimeOnly(23, 59), slots).Should().Be(1m);
    }

    [Fact]
    public void As_needed_slots_are_never_due()
    {
        var slots = new[] { Slot(1m, new TimeOnly(6, 0), asNeeded: true), Slot(1m, new TimeOnly(7, 0)) };

        Due(new TimeOnly(12, 0), slots).Should().Be(1m);
    }

    [Theory]
    [InlineData(1, 7, 59, 0)]
    [InlineData(1, 8, 0, 1)]
    [InlineData(2, 12, 0, 1)]
    [InlineData(2, 20, 0, 2)]
    [InlineData(3, 13, 0, 2)]
    [InlineData(4, 16, 30, 3)]
    public void Without_slots_the_doses_follow_the_default_times(int perDay, int hour, int minute, int expected)
    {
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, perDay) };

        Due(new TimeOnly(hour, minute), schedule: schedule).Should().Be(expected);
    }

    [Fact]
    public void More_than_four_a_day_without_slots_waits_for_the_end_of_day()
    {
        var schedule = new[] { DomainFactory.Schedule(new DateOnly(2026, 9, 1), 1m, 5) };

        Due(new TimeOnly(23, 59), schedule: schedule).Should().Be(0m);
    }

    [Fact]
    public void Other_schedules_place_the_whole_day_at_the_single_dose_time()
    {
        var schedule = new[]
        {
            DomainFactory.ScheduleFor(new DateOnly(2026, 9, 1), new CyclicSchedule(21, 7, 2m)),
        };

        Due(new TimeOnly(7, 0), schedule: schedule).Should().Be(0m);
        Due(new TimeOnly(8, 0), schedule: schedule).Should().Be(2m);
    }

    [Fact]
    public void Prn_suspended_inactive_and_not_started_medicines_have_nothing_due()
    {
        var prn = new[] { DomainFactory.ScheduleFor(new DateOnly(2026, 9, 1), new PrnSchedule()) };
        var noon = new TimeOnly(12, 0);

        Due(noon, schedule: prn).Should().Be(0m);
        Due(noon, suspensions: [DomainFactory.Suspension(Today)]).Should().Be(0m);
        Due(noon, medicine: DomainFactory.Medicine(isActive: false)).Should().Be(0m);
        Due(noon, medicine: DomainFactory.Medicine(startDate: Today.AddDays(1))).Should().Be(0m);
    }

    [Fact]
    public void A_day_the_ledger_already_booked_has_nothing_due()
    {
        Due(new TimeOnly(23, 0), booked: true).Should().Be(0m);
    }

    [Fact]
    public void A_time_skipped_by_daylight_saving_is_due_after_it()
    {
        // 02:30 does not exist on the spring-forward day: the clock reads
        // 03:00 right after 01:59.
        var slots = new[] { Slot(1m, new TimeOnly(2, 30)) };

        Due(new TimeOnly(3, 0), slots).Should().Be(1m);
    }
}
