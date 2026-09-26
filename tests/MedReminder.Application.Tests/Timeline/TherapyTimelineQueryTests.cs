using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.Timeline;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Timeline;

public class TherapyTimelineQueryTests
{
    private static TherapyTimelineQuery NewQuery(ApplicationTestScope scope)
        => new(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions, scope.Slots, scope.Clock);

    private static Task<Guid> SeedAsync(ApplicationTestScope scope, string name = "Enalapril")
        => scope.AddMedicine.ExecuteAsync(
            new AddMedicineCommand(
                name, "tablets", 1m, 2,
                new DateOnly(2026, 9, 1), 7,
                NotificationChannels.Windows,
                InitialQuantity: 30m),
            CancellationToken.None);

    [Fact]
    public async Task Loads_the_default_window_around_the_local_today()
    {
        var scope = new ApplicationTestScope();
        await SeedAsync(scope);

        var timeline = await NewQuery(scope).LoadAsync(null, CancellationToken.None);

        timeline.Today.Should().Be(new DateOnly(2026, 9, 13));
        timeline.Window.Should().Be(TimelineWindow.Around(new DateOnly(2026, 9, 13)));
        timeline.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Uses_the_requested_window()
    {
        var scope = new ApplicationTestScope();
        await SeedAsync(scope);
        var window = new TimelineWindow(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        var timeline = await NewQuery(scope).LoadAsync(window, CancellationToken.None);

        timeline.Window.Should().Be(window);
        timeline.Rows[0].Segments.Should().ContainSingle()
            .Which.Start.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task Forecast_equals_the_one_computed_for_the_table_from_the_same_data()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 11, 1), null),
            CancellationToken.None);

        var timeline = await NewQuery(scope).LoadAsync(null, CancellationToken.None);

        var today = new DateOnly(2026, 9, 13);
        var stock = MedicineStock.Current(await scope.Stock.ListForMedicineAsync(id, CancellationToken.None));
        var expected = MedicineForecast.Compute(
            today, stock,
            await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None),
            await scope.Slots.ListForMedicineAsync(id, CancellationToken.None),
            await scope.Suspensions.ListForMedicineAsync(id, CancellationToken.None));

        var row = timeline.Rows.Should().ContainSingle().Subject;
        row.CurrentStock.Should().Be(stock);
        row.Forecast.Should().Be(expected);
        row.Forecast.RunOut.EstimatedRunOutDate.Should().Be(new DateOnly(2026, 9, 28));
        row.Segments[^1].Kind.Should().Be(TimelineSegmentKind.Suspended);
    }
}
