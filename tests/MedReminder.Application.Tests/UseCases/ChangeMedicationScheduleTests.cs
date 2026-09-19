using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Medicines;
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

    // ---------- A1 schedule payload --------------------------------

    [Fact]
    public async Task Persists_new_schedule_shape_when_supplied()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        var tapering = new TaperingSchedule(4m, 0.5m, 0.5m, 7);
        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(
                id,
                NewDosePerAdministration: 4m,     // display value
                NewAdministrationsPerDay: 1,
                EffectiveFrom: new DateOnly(2026, 9, 20),
                NewSchedule: tapering),
            CancellationToken.None);

        var history = await scope.Schedules.ListForMedicineAsync(id, CancellationToken.None);
        history.Should().HaveCount(2);
        var latest = history.OrderByDescending(h => h.EffectiveFrom).First();
        latest.ScheduleKind.Should().Be(ScheduleKind.Tapering);
        latest.SchedulePayload.Should().NotBeNullOrWhiteSpace();

        var restored = ScheduleCodec.Deserialize(
            latest.ScheduleKind, latest.SchedulePayload,
            latest.DosePerAdministration, latest.AdministrationsPerDay);
        restored.Should().Be(tapering);
    }

    [Fact]
    public async Task Accepts_zero_display_dose_when_new_schedule_is_prn()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await scope.ChangeMedicationSchedule.ExecuteAsync(
            new ChangeMedicationScheduleCommand(
                id, 0m, 1, new DateOnly(2026, 9, 20), new PrnSchedule()),
            CancellationToken.None);

        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.DosePerAdministration.Should().Be(0m);
        medicine.AdministrationsPerDay.Should().Be(1);
    }
}
