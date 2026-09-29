using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class MedicineForecastTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);
    private static readonly Guid MedicineId = Guid.NewGuid();

    private static MedicationScheduleHistory Entry(DateOnly from, decimal dose, int admin)
        => new()
        {
            MedicineId = MedicineId,
            EffectiveFrom = from,
            DosePerAdministration = dose,
            AdministrationsPerDay = admin,
        };

    [Fact]
    public void Composes_rate_suspension_and_run_out()
    {
        var history = new[] { Entry(new DateOnly(2026, 9, 1), 1m, 2) };

        var result = MedicineForecast.Compute(Today, 30m, history, null, []);

        result.DailyRate.Should().Be(2m);
        result.IsSuspendedToday.Should().BeFalse();
        result.RunOut.Should().Be(RunOutForecast.Compute(Today, 30m, 2m, false));
    }

    [Fact]
    public void Slots_override_the_schedule_rate()
    {
        var history = new[] { Entry(new DateOnly(2026, 9, 1), 1m, 2) };
        var slots = new[]
        {
            new MedicationAdministrationSlot { MedicineId = MedicineId, Dose = 3m },
        };

        var result = MedicineForecast.Compute(Today, 30m, history, slots, []);

        result.DailyRate.Should().Be(3m);
        result.RunOut.DaysRemaining.Should().Be(10);
    }

    [Fact]
    public void Suspended_today_gives_no_run_out()
    {
        var history = new[] { Entry(new DateOnly(2026, 9, 1), 1m, 2) };
        var suspensions = new[]
        {
            new MedicationSuspension { MedicineId = MedicineId, StartDate = new DateOnly(2026, 9, 10) },
        };

        var result = MedicineForecast.Compute(Today, 30m, history, null, suspensions);

        result.IsSuspendedToday.Should().BeTrue();
        result.RunOut.EstimatedRunOutDate.Should().BeNull();
    }
}
