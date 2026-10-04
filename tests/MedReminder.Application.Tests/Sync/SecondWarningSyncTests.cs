using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Sync;

// Second low-stock warning (docs/notes/EVOLUTION-PROPOSALS-2.md §3.1): the
// stage of a sent email reaches the other devices of the sync group, so
// a second-stage email is sent once per group, like the first.
public class SecondWarningSyncTests
{
    private readonly ApplicationTestScope _a = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationTestScope _b = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, 500, TimeSpan.Zero));

    public SecondWarningSyncTests()
    {
        var group = _a.EnableSync(Guid.Parse("0a000000-0000-0000-0000-000000000000"));
        _b.SyncSettingsStore.Save(group with { DeviceId = Guid.Parse("0b000000-0000-0000-0000-000000000000") });
    }

    [Fact]
    public async Task A_second_stage_email_sent_by_one_device_is_not_sent_by_another()
    {
        // Stock 6, rate 2, threshold 7: 3 days, second stage.
        var id = await _a.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "compresse",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 2,
            StartDate: new DateOnly(2026, 9, 13),
            ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Email,
            EndDate: null,
            InitialQuantity: 6m), default);
        await _a.Monitor.RunAsync(default);
        _a.Email.Sent.Should().ContainSingle();

        await _b.ApplyRemote.ExecuteAsync(await _a.SyncOperations.ListAllAsync(default), default);

        _b.SentEmails.All.Should().ContainSingle(e => e.MedicineId == id)
            .Which.Stage.Should().Be(NotificationCycle.SecondStage);
        await _b.Monitor.RunAsync(default);
        _b.Email.Sent.Should().BeEmpty();
        // B reached the second stage itself: the replicated email closed it.
        var evt = _b.Notifications.All.Should().ContainSingle().Subject;
        evt.Stage.Should().Be(NotificationCycle.SecondStage);
        evt.Success.Should().BeTrue();
    }
}
