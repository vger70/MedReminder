using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class SuspendResumeMedicationTests
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
    public async Task Suspend_creates_open_suspension()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        var suspensionId = await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 15), Reason: "pausa"),
            CancellationToken.None);

        var open = await scope.Suspensions.GetOpenSuspensionAsync(id, CancellationToken.None);
        open.Should().NotBeNull();
        open!.Id.Should().Be(suspensionId);
        open.EndDate.Should().BeNull();
        open.Reason.Should().Be("pausa");
    }

    [Fact]
    public async Task Suspending_twice_throws()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 15)),
            CancellationToken.None);

        await FluentActions.Awaiting(() =>
            scope.SuspendMedication.ExecuteAsync(
                new SuspendMedicationCommand(id, new DateOnly(2026, 9, 16)),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Resume_closes_the_open_suspension()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 15)),
            CancellationToken.None);

        await scope.ResumeMedication.ExecuteAsync(
            new ResumeMedicationCommand(id, new DateOnly(2026, 9, 20)),
            CancellationToken.None);

        var open = await scope.Suspensions.GetOpenSuspensionAsync(id, CancellationToken.None);
        open.Should().BeNull();

        var all = await scope.Suspensions.ListForMedicineAsync(id, CancellationToken.None);
        all.Should().ContainSingle().Which.EndDate.Should().Be(new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task Resume_without_open_suspension_throws()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);

        await FluentActions.Awaiting(() =>
            scope.ResumeMedication.ExecuteAsync(
                new ResumeMedicationCommand(id, new DateOnly(2026, 9, 20)),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Resume_with_end_date_before_start_throws()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 15)),
            CancellationToken.None);

        await FluentActions.Awaiting(() =>
            scope.ResumeMedication.ExecuteAsync(
                new ResumeMedicationCommand(id, new DateOnly(2026, 9, 10)),
                CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Second_suspension_after_resume_is_allowed()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 15)),
            CancellationToken.None);
        await scope.ResumeMedication.ExecuteAsync(
            new ResumeMedicationCommand(id, new DateOnly(2026, 9, 20)),
            CancellationToken.None);

        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 25)),
            CancellationToken.None);

        var all = await scope.Suspensions.ListForMedicineAsync(id, CancellationToken.None);
        all.Should().HaveCount(2);
    }
}
