using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

// Issue #313. Today is a Monday.
public class RunOutSimulationTests
{
    private static readonly DateOnly Today = new(2026, 11, 2);
    private static readonly Guid MedicineId = Guid.NewGuid();

    private static Medicine Med(DateOnly? start = null, DateOnly? end = null) => new()
    {
        Id = MedicineId,
        Name = "x",
        Unit = "tablets",
        StartDate = start ?? Today.AddDays(-400),
        EndDate = end,
    };

    private static MedicationScheduleHistory Row(Schedule schedule, DateOnly from)
    {
        var (kind, payload) = ScheduleCodec.Serialize(schedule);
        var fixedDaily = schedule as FixedDailySchedule;
        return new MedicationScheduleHistory
        {
            MedicineId = MedicineId,
            EffectiveFrom = from,
            DosePerAdministration = fixedDaily?.DosePerAdministration ?? 1m,
            AdministrationsPerDay = fixedDaily?.AdministrationsPerDay ?? 1,
            ScheduleKind = kind,
            SchedulePayload = payload,
        };
    }

    private static SteppedTaperingSchedule Stepped(bool maintain, params (decimal Dose, int Days)[] stages)
        => new([.. stages.Select(s => new TaperStage(s.Dose, s.Days))], maintain);

    private static MedicationSuspension Suspension(int from, int? to) => new()
    {
        MedicineId = MedicineId,
        StartDate = Today.AddDays(from),
        EndDate = to is { } days ? Today.AddDays(days) : null,
    };

    private static RunOutForecastResult Walk(
        Schedule schedule,
        decimal stock,
        int anchor = 0,
        Medicine? medicine = null,
        IReadOnlyList<MedicationSuspension>? suspensions = null,
        int offset = 0,
        bool booked = false)
        => RunOutSimulation.Compute(medicine ?? Med(), Today.AddDays(offset), stock, booked,
            [Row(schedule, Today.AddDays(anchor))], null, suspensions ?? []);

    public static TheoryData<string, decimal, int, int?> Regimens() => new()
    {
        { "stepped down, maintained", 60m, 0, 32 },
        { "stepped up", 40m, 0, 22 },
        { "linear taper", 200m, 0, 30 },
        { "cycle, first pause day", 10m, -21, 17 },
        { "cycle, last dose day", 5m, -20, 12 },
        { "weekly, a dose day", 6m, -30, 14 },
        { "alternate days, a dose day", 10m, 0, 10 },
        { "alternate days, a day without", 10m, -1, 11 },
        { "bounded course, stock left", 44m, 0, null },
    };

    [Theory]
    [MemberData(nameof(Regimens))]
    public void Follows_the_regimen_day_by_day(string regimen, decimal stock, int anchor, int? days)
    {
        Schedule schedule = regimen switch
        {
            "stepped down, maintained" => Stepped(true, (4m, 7), (2m, 7), (1m, 7)),
            "stepped up" => Stepped(true, (1m, 14), (3m, 7)),
            "linear taper" => new TaperingSchedule(10m, 2m, 2m, 7),
            "cycle, first pause day" or "cycle, last dose day" => new CyclicSchedule(21, 7, 1m),
            "weekly, a dose day" => new WeeklySchedule([1m, 0m, 1m, 0m, 1m, 0m, 0m]),
            "bounded course, stock left" => Stepped(false, (4m, 7), (2m, 7)),
            _ => new CyclicSchedule(1, 1, 2m),
        };

        var result = Walk(schedule, stock, anchor);

        result.DaysRemaining.Should().Be(days);
        result.EstimatedRunOutDate.Should().Be(days is { } d ? Today.AddDays(d) : null);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(.5, 0)]
    [InlineData(5, 2)]
    [InlineData(30, 15)]
    [InlineData(1000, 500)]
    [InlineData(100001, 50000)]
    public void A_fixed_regimen_gives_the_division_also_past_the_horizon(decimal stock, int days)
    {
        var result = Walk(new FixedDailySchedule(1m, 2), stock, -30);

        result.DaysRemaining.Should().Be(days);
        result.EstimatedRunOutDate.Should().Be(Today.AddDays(days));
    }

    [Fact]
    public void Slots_set_the_quantity_of_a_fixed_regimen()
    {
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = MedicineId, Dose = 1m },
            new MedicationAdministrationSlot { MedicineId = MedicineId, Dose = .5m },
        };

        var result = RunOutSimulation.Compute(Med(), Today, 30m, false,
            [Row(new FixedDailySchedule(1m, 1), Today.AddDays(-30))], slots, []);

        result.DaysRemaining.Should().Be(20);
    }

    [Fact]
    public void A_later_change_of_schedule_counts()
    {
        var result = RunOutSimulation.Compute(Med(), Today, 20m, false,
            [Row(new FixedDailySchedule(1m, 1), Today.AddDays(-30)), Row(new FixedDailySchedule(2m, 1), Today.AddDays(5))],
            null, []);

        result.DaysRemaining.Should().Be(12);
    }

    [Fact]
    public void The_therapy_end_is_left_to_the_callers()
    {
        var result = Walk(new FixedDailySchedule(1m, 1), 30m, -30, Med(end: Today.AddDays(10)));

        result.EstimatedRunOutDate.Should().Be(Today.AddDays(30));
    }

    [Fact]
    public void A_start_ahead_delays_the_consumption()
    {
        var result = Walk(new FixedDailySchedule(1m, 1), 5m, -30, Med(start: Today.AddDays(10)));

        result.DaysRemaining.Should().Be(15);
    }

    [Theory]
    [InlineData(0, 9)]
    [InlineData(0, null)]
    public void A_suspension_in_progress_gives_no_estimate(int from, int? to)
    {
        var result = Walk(new FixedDailySchedule(1m, 1), 4m, -30, suspensions: [Suspension(from, to)]);

        result.Should().Be(new RunOutForecastResult(null, null));
    }

    [Fact]
    public void A_suspension_ahead_moves_the_date()
    {
        Walk(new FixedDailySchedule(1m, 1), 20m, -30, suspensions: [Suspension(5, 14)])
            .DaysRemaining.Should().Be(30);
    }

    [Fact]
    public void An_open_suspension_ahead_gives_no_estimate()
    {
        Walk(new FixedDailySchedule(1m, 1), 20m, -30, suspensions: [Suspension(5, null)])
            .Should().Be(new RunOutForecastResult(null, null));
    }

    [Fact]
    public void As_needed_gives_no_estimate()
    {
        Walk(new PrnSchedule(), 10m, -30).Should().Be(new RunOutForecastResult(null, null));
    }

    [Fact]
    public void No_stock_gives_today_on_a_dose_day_and_the_next_dose_on_a_pause()
    {
        Walk(new CyclicSchedule(21, 7, 1m), 0m, -20).Should().Be(new RunOutForecastResult(0, Today));
        Walk(new CyclicSchedule(21, 7, 1m), 0m, -21).Should().Be(new RunOutForecastResult(7, Today.AddDays(7)));
    }

    [Theory]
    [InlineData(30, 1, 29, 30)]
    [InlineData(60, 4, 31, 32)]
    public void A_booked_day_keeps_the_date_and_counts_the_days_after_it(decimal stock, decimal booked, int days, int date)
    {
        Schedule schedule = booked == 1m ? new FixedDailySchedule(1m, 1) : Stepped(true, (4m, 7), (2m, 7), (1m, 7));
        var anchor = booked == 1m ? -30 : 0;

        Walk(schedule, stock, anchor).EstimatedRunOutDate.Should().Be(Today.AddDays(date));

        var after = Walk(schedule, stock - booked, anchor, booked: true);
        after.DaysRemaining.Should().Be(days);
        after.EstimatedRunOutDate.Should().Be(Today.AddDays(date));
    }

    [Fact]
    public void The_date_stays_while_the_stock_is_consumed()
    {
        var schedule = new WeeklySchedule([1m, 0m, 1m, 0m, 1m, 0m, 0m]);
        var row = Row(schedule, Today.AddDays(-30));
        var first = Today.AddDays(14);
        var stock = 6m;

        for (var i = 0; i <= 14; i++)
        {
            var day = Today.AddDays(i);
            var result = RunOutSimulation.Compute(Med(), day, stock, false, [row], null, []);
            result.EstimatedRunOutDate.Should().Be(first, $"day {i}");
            result.DaysRemaining.Should().Be(14 - i, $"day {i}");
            stock -= schedule.RateOn(day, row.EffectiveFrom);
        }
    }

    [Fact]
    public void Days_covered_from_tomorrow_divide_on_a_fixed_regimen()
    {
        RunOutSimulation.DaysCoveredFrom(Med(), Today.AddDays(1), 23m, [Row(new FixedDailySchedule(1m, 2), Today.AddDays(-30))], null, [])
            .Should().Be(11);
    }

    [Fact]
    public void A_stock_and_a_dose_past_any_date_stop_at_the_last_date()
    {
        var result = Walk(new FixedDailySchedule(.01m, 1), 100000m, -30);

        result.DaysRemaining.Should().Be(DateOnly.MaxValue.DayNumber - Today.DayNumber);
        result.EstimatedRunOutDate.Should().Be(DateOnly.MaxValue);
    }
}
