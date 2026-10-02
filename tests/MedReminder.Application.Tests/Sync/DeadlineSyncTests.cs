using FluentAssertions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// Deadlines between two devices of a sync group: one register per
// deadline, last writer wins, deletion included; a deadline of the
// profile needs no medicine.
public class DeadlineSyncTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, 500, TimeSpan.Zero));
    private readonly Guid _medicine;

    public DeadlineSyncTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
        _medicine = _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 30m), default).GetAwaiter().GetResult();
    }

    private async Task ExchangeAsync()
    {
        await _b.ApplyRemote.ExecuteAsync(
            (await _a.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_b)).ToList(), default);
        await _a.ApplyRemote.ExecuteAsync(
            (await _b.SyncOperations.ListAllAsync(default)).Where(o => o.DeviceId != Device(_a)).ToList(), default);
    }

    private static Guid Device(ApplicationTestScope scope) => scope.SyncSettingsStore.Load()!.DeviceId;

    private SaveDeadlineCommand Command(Guid? id, int leadDays, Guid? medicine = null)
        => new(id, medicine ?? _medicine, DeadlineKind.TherapeuticPlan, "Plan", Today.AddDays(60), leadDays, 12,
            NotificationChannels.Both, null);

    [Fact]
    public async Task A_deadline_reaches_the_other_device()
    {
        var id = await _a.SaveDeadline.ExecuteAsync(Command(null, 30), default);

        await ExchangeAsync();

        var copy = _b.Deadlines.All.Should().ContainSingle().Subject;
        copy.Id.Should().Be(id);
        copy.MedicineId.Should().Be(_medicine);
        copy.Label.Should().Be("Plan");
        copy.LeadDays.Should().Be(30);
        copy.RepeatMonths.Should().Be(12);
        copy.Channels.Should().Be(NotificationChannels.Both);
    }

    [Fact]
    public async Task A_deadline_of_the_profile_reaches_the_other_device()
    {
        var id = await _a.SaveDeadline.ExecuteAsync(
            new SaveDeadlineCommand(null, null, DeadlineKind.ExemptionRenewal, null, Today.AddDays(90), 30, null,
                NotificationChannels.Windows, null), default);

        await ExchangeAsync();

        var copy = _b.Deadlines.All.Should().ContainSingle().Subject;
        copy.Id.Should().Be(id);
        copy.MedicineId.Should().BeNull();
        copy.Kind.Should().Be(DeadlineKind.ExemptionRenewal);
    }

    [Fact]
    public async Task Concurrent_changes_end_with_the_later_one_on_both_devices()
    {
        var id = await _a.SaveDeadline.ExecuteAsync(Command(null, 30), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        _b.Clock.AdvanceBy(TimeSpan.FromMinutes(2));
        await _a.SaveDeadline.ExecuteAsync(Command(id, 20), default);
        await _b.CompleteDeadline.ExecuteAsync(id, Today, default);
        await ExchangeAsync();

        foreach (var scope in new[] { _a, _b })
        {
            var d = scope.Deadlines.All.Should().ContainSingle().Subject;
            d.LeadDays.Should().Be(30, "B's later write held the state B had");
            d.DueOn.Should().Be(Today.AddDays(60).AddMonths(12));
        }
    }

    [Fact]
    public async Task A_deletion_reaches_the_other_device()
    {
        var id = await _a.SaveDeadline.ExecuteAsync(Command(null, 30), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        await _a.DeleteDeadline.ExecuteAsync(id, default);
        await ExchangeAsync();

        _a.Deadlines.All.Should().BeEmpty();
        _b.Deadlines.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_deadlines_on_the_other_device()
    {
        // A medicine with no recorded stock can be deleted.
        var mistake = await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Mistake", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows), default);
        await _a.SaveDeadline.ExecuteAsync(Command(null, 30, mistake), default);
        var own = await _a.SaveDeadline.ExecuteAsync(Command(null, 30), default);
        await ExchangeAsync();

        _a.Clock.AdvanceBy(TimeSpan.FromMinutes(1));
        (await _a.DeleteMedicine.ExecuteAsync(new DeleteMedicineCommand(mistake), default))
            .Should().Be(DeleteMedicineOutcome.Deleted);
        await ExchangeAsync();

        _b.Deadlines.All.Should().ContainSingle().Which.Id.Should().Be(own);
    }
}
