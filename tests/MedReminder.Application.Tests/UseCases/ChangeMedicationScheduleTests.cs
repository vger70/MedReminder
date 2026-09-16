using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class ChangeMedicationScheduleTests
{
    private static async Task<Guid> SeedAsync(ApplicationTestScope scope)
    {
        var cmd = new AddMedicineCommand(
            "Enalapril", "compresse", 1m, 2,
            new DateOnly(2026, 9, 1), 7,
            NotificationChannels.Windows,
            InitialQuantity: 30m);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Adds_new_schedule_entry_and_updates_current_fields()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(id, 2m, 3, new DateOnly(2026, 9, 20)),
            CancellationToken.None);

        var schedule = await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        schedule.Should().HaveCount(2);

        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.DosePerAdministration.Should().Be(2m);
        medicine.AdministrationsPerDay.Should().Be(3);
    }

    [Fact]
    public async Task Rejects_effective_from_before_therapy_start()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await FluentActions.Awaiting(() =>
            scope.ChangeMedicationSchedule.ExecuteAsync(
                new ChangeMedicationScheduleCommand(id, 2m, 3, new DateOnly(2026, 8, 15)),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Rejects_non_positive_values()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await FluentActions.Awaiting(() =>
            scope.ChangeMedicationSchedule.ExecuteAsync(
                new ChangeMedicationScheduleCommand(id, 0m, 3, new DateOnly(2026, 9, 20)),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        await FluentActions.Awaiting(() =>
            scope.ChangeMedicationSchedule.ExecuteAsync(
                new ChangeMedicationScheduleCommand(id, 1m, 0, new DateOnly(2026, 9, 20)),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }
}
