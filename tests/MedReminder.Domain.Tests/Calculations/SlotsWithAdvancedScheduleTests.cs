using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Slots under a non-FixedDaily schedule
// (docs/analysis/ANALYSIS-SLOTS-ADVANCED-SCHEDULES.md): the schedule
// says how much, the slots say when, splitting the day's quantity in
// proportion to their doses.
public class SlotsWithAdvancedScheduleTests
{
    private static readonly DateOnly From = new(2026, 9, 1);

    private static MedicationAdministrationSlot Slot(decimal dose, int hour, bool asNeeded = false)
        => new() { MedicineId = DomainFactory.MedicineId, Dose = dose, Time = new TimeOnly(hour, 0), IsAsNeeded = asNeeded };

    // Taper 4 → 1, one step down every 2 days: 4 on 09-01, 3 on 09-03.
    private static readonly MedicationScheduleHistory[] Taper =
        [DomainFactory.ScheduleFor(From, new TaperingSchedule(4m, 1m, 1m, 2))];

    [Fact]
    public void Equal_slots_split_the_tapering_quantity_evenly()
    {
        var slots = new[] { Slot(1m, 8), Slot(1m, 20) };

        DailyConsumption.SlotQuantities(new DateOnly(2026, 9, 1), Taper, slots)
            .Select(x => x.Quantity).Should().Equal(2m, 2m);
        DailyConsumption.SlotQuantities(new DateOnly(2026, 9, 3), Taper, slots)
            .Select(x => x.Quantity).Should().Equal(1.5m, 1.5m);
    }

    [Fact]
    public void Slot_doses_are_weights()
    {
        var slots = new[] { Slot(2m, 8), Slot(1m, 20) };

        var quantities = DailyConsumption.SlotQuantities(From, Taper, slots).Select(x => x.Quantity).ToList();

        quantities.Sum().Should().Be(4m);
        quantities[0].Should().BeApproximately(8m / 3m, 0.0000001m);
    }

    [Fact]
    public void A_cyclic_pause_day_has_nothing_to_split()
    {
        var cyclic = new[] { DomainFactory.ScheduleFor(From, new CyclicSchedule(3, 2, 2m)) };
        var slots = new[] { Slot(1m, 8), Slot(1m, 20) };

        DailyConsumption.SlotQuantities(new DateOnly(2026, 9, 4), cyclic, slots)
            .Select(x => x.Quantity).Should().Equal(0m, 0m);
        DailyConsumption.SlotQuantities(new DateOnly(2026, 9, 1), cyclic, slots)
            .Select(x => x.Quantity).Should().Equal(1m, 1m);
    }

    [Fact]
    public void As_needed_slots_take_no_share()
    {
        var slots = new[] { Slot(1m, 8), Slot(5m, 12, asNeeded: true) };

        DailyConsumption.SlotQuantities(From, Taper, slots)
            .Select(x => x.Quantity).Should().Equal(4m, 0m);
        DailyConsumption.RateOn(From, Taper, slots).Should().Be(4m);
    }

    [Fact]
    public void Only_as_needed_slots_consume_nothing()
    {
        var slots = new[] { Slot(1m, 8, asNeeded: true) };

        DailyConsumption.RateOn(From, Taper, slots).Should().Be(0m);
    }

    [Fact]
    public void A_fixed_daily_schedule_keeps_the_slot_doses()
    {
        var fixedDaily = new[] { DomainFactory.Schedule(From, 1m, 2) };
        var slots = new[] { Slot(2m, 8), Slot(1m, 20) };

        DailyConsumption.SlotQuantities(From, fixedDaily, slots)
            .Select(x => x.Quantity).Should().Equal(2m, 1m);
        DailyConsumption.RateOn(From, fixedDaily, slots).Should().Be(3m);
    }

    [Fact]
    public void The_doses_due_so_far_follow_the_shares()
    {
        var slots = new[] { Slot(1m, 8), Slot(1m, 20) };

        IntradayConsumption.DueSoFar(
            DomainFactory.Medicine(startDate: From), From, new TimeOnly(9, 0), Taper, [], slots,
            dayAlreadyBooked: false, _ => null, DoseTimeDefault.BuiltIn)
            .Should().Be(2m);
    }
}
