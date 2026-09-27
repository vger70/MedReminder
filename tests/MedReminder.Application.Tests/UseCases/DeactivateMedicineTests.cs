using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.UseCases;

public class DeactivateMedicineTests
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
    public async Task Deactivate_clears_IsActive_and_stamps_UpdatedAt()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        var later = new DateTimeOffset(2026, 9, 20, 8, 30, 0, TimeSpan.Zero);
        scope.Clock.SetUtcNow(later);
        var savesBefore = scope.Uow.SaveChangesCalls;

        var found = await scope.DeactivateMedicine.ExecuteAsync(
            new DeactivateMedicineCommand(id), CancellationToken.None);

        found.Should().BeTrue();
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        medicine!.IsActive.Should().BeFalse();
        medicine.UpdatedAt.Should().Be(later);
        scope.Uow.SaveChangesCalls.Should().Be(savesBefore + 1);
    }

    [Fact]
    public async Task Deactivate_leaves_the_other_fields_untouched()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        var medicine = await scope.Medicines.GetAsync(id, CancellationToken.None);
        // A value UpdateMedicine would trim: the deactivate action must
        // not normalise it.
        medicine!.Name = " Enalapril ";
        medicine.Notes = "  ";

        await scope.DeactivateMedicine.ExecuteAsync(
            new DeactivateMedicineCommand(id), CancellationToken.None);

        var after = await scope.Medicines.GetAsync(id, CancellationToken.None);
        after!.Name.Should().Be(" Enalapril ");
        after.Notes.Should().Be("  ");
        after.DosePerAdministration.Should().Be(1m);
        after.AdministrationsPerDay.Should().Be(2);
        after.StartDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task Deactivating_an_inactive_medicine_still_saves()
    {
        var scope = new ApplicationTestScope();
        var id = await SeedAsync(scope);
        await scope.DeactivateMedicine.ExecuteAsync(
            new DeactivateMedicineCommand(id), CancellationToken.None);
        var savesBefore = scope.Uow.SaveChangesCalls;

        var found = await scope.DeactivateMedicine.ExecuteAsync(
            new DeactivateMedicineCommand(id), CancellationToken.None);

        found.Should().BeTrue();
        scope.Uow.SaveChangesCalls.Should().Be(savesBefore + 1);
    }

    [Fact]
    public async Task Deactivating_a_missing_medicine_is_a_no_op()
    {
        var scope = new ApplicationTestScope();

        var found = await scope.DeactivateMedicine.ExecuteAsync(
            new DeactivateMedicineCommand(Guid.NewGuid()), CancellationToken.None);

        found.Should().BeFalse();
        scope.Uow.SaveChangesCalls.Should().Be(0);
    }

    [Fact]
    public async Task Null_command_throws()
    {
        var scope = new ApplicationTestScope();

        await FluentActions.Awaiting(() =>
            scope.DeactivateMedicine.ExecuteAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
    }
}
