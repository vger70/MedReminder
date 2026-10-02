using FluentAssertions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Notifications;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Deadlines;

// Administrative deadlines (docs/notes/EVOLUTION-PROPOSALS-2.md §3.6):
// save, complete, delete, list and the reminder.
public class DeadlineUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static async Task<Guid> AddMedicineAsync(ApplicationTestScope scope, string name = "Enalapril")
        => await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 100m), default);

    private static SaveDeadlineCommand Plan(
        Guid? medicineId, DateOnly dueOn, int? repeatMonths = null, Guid? id = null,
        NotificationChannels channels = NotificationChannels.Both)
        => new(id, medicineId, DeadlineKind.TherapeuticPlan, null, dueOn, 10, repeatMonths, channels, null);

    [Fact]
    public async Task A_saved_deadline_is_recorded_and_replicated_as_a_whole()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);

        var id = await scope.SaveDeadline.ExecuteAsync(Plan(medicine, Today.AddDays(60)), default);
        await scope.SaveDeadline.ExecuteAsync(
            Plan(medicine, Today.AddDays(60), id: id) with { Label = "  Plan AIFA  " }, default);

        var saved = scope.Deadlines.All.Should().ContainSingle().Subject;
        saved.Label.Should().Be("Plan AIFA");
        saved.MedicineId.Should().Be(medicine);
        var ops = scope.SyncOperations.All.Where(o => o.Type == nameof(DeadlineChanged)).ToList();
        ops.Should().HaveCount(2).And.OnlyContain(o => o.SchemaVersion == 8 && o.EntityId == id && o.MedicineId == medicine);
        var winner = await scope.Registers.WinnerAsync(id, SyncRegisters.DeadlineState, default);
        SyncRegisters.ParseDeadline(winner!.Value!).Label.Should().Be("Plan AIFA");
    }

    [Fact]
    public async Task A_deadline_of_the_profile_touches_no_medicine()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();

        await scope.SaveDeadline.ExecuteAsync(
            new SaveDeadlineCommand(null, null, DeadlineKind.ExemptionRenewal, null, Today.AddDays(90), 30, 12,
                NotificationChannels.Windows, null), default);

        scope.Deadlines.All.Should().ContainSingle().Which.MedicineId.Should().BeNull();
        scope.SyncOperations.All.Should().ContainSingle(o => o.Type == nameof(DeadlineChanged))
            .Which.MedicineId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task A_deadline_of_kind_other_needs_a_description()
    {
        var scope = new ApplicationTestScope(Now);

        var act = () => scope.SaveDeadline.ExecuteAsync(
            Plan(null, Today) with { Kind = DeadlineKind.Other, Label = "  " }, default);

        (await act.Should().ThrowAsync<InvalidDeadlineException>())
            .Which.Error.Should().Be(DeadlineError.NoLabel);
        scope.Deadlines.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Completing_closes_a_one_off_deadline_and_moves_a_recurring_one()
    {
        var scope = new ApplicationTestScope(Now);
        var once = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(-3)), default);
        var yearly = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(-3), repeatMonths: 12), default);

        await scope.CompleteDeadline.ExecuteAsync(once, Today, default);
        await scope.CompleteDeadline.ExecuteAsync(yearly, Today, default);

        var closed = scope.Deadlines.All.Single(d => d.Id == once);
        closed.DoneOn.Should().Be(Today);
        closed.DueOn.Should().Be(Today.AddDays(-3));
        var moved = scope.Deadlines.All.Single(d => d.Id == yearly);
        moved.DoneOn.Should().BeNull();
        moved.DueOn.Should().Be(Today.AddDays(-3).AddMonths(12), "the series keeps its dates");
        moved.LeadDays.Should().Be(10);
    }

    [Fact]
    public async Task Deleting_records_a_deletion()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var id = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today), default);

        await scope.DeleteDeadline.ExecuteAsync(id, default);

        scope.Deadlines.All.Should().BeEmpty();
        var last = scope.SyncOperations.All.Last(o => o.Type == nameof(DeadlineChanged));
        ((DeadlineChanged)OperationCodec.Deserialize(last.Type, last.SchemaVersion, last.Payload))
            .Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task The_list_shows_the_deadlines_to_act_on_first()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope, "Bisoprolol");
        var upcoming = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(40)), default);
        var done = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(-20)), default);
        await scope.CompleteDeadline.ExecuteAsync(done, Today.AddDays(-21), default);
        var soon = await scope.SaveDeadline.ExecuteAsync(Plan(medicine, Today.AddDays(5)), default);
        var overdue = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(-1)), default);

        var list = await scope.DeadlineList.LoadAsync(default);

        list.Select(i => i.Deadline.Id).Should().Equal(overdue, soon, upcoming, done);
        list.Select(i => i.Status).Should().Equal(
            DeadlineStatus.Overdue, DeadlineStatus.DueSoon, DeadlineStatus.Upcoming, DeadlineStatus.Done);
        list[1].MedicineName.Should().Be("Bisoprolol");
        list[0].MedicineName.Should().BeNull();
    }

    [Fact]
    public async Task The_reminder_is_shown_once_per_date()
    {
        var scope = new ApplicationTestScope(Now);
        var id = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(15), repeatMonths: 6), default);

        (await scope.DeadlineReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(0, "too early");
        (await scope.DeadlineReminders.RunAsync(Today.AddDays(5), sendsEmail: true, default)).Should().Be(1);
        (await scope.DeadlineReminders.RunAsync(Today.AddDays(20), sendsEmail: true, default)).Should().Be(0);
        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().ContainSingle();

        // The next date of the series gets its own reminder.
        await scope.CompleteDeadline.ExecuteAsync(id, Today.AddDays(20), default);
        var next = Today.AddDays(15).AddMonths(6);
        (await scope.DeadlineReminders.RunAsync(next.AddDays(-10), sendsEmail: true, default)).Should().Be(1);
        scope.DeadlineReminderEvents.All.Select(e => e.DueOn).Should().Equal(Today.AddDays(15), next);
    }

    [Fact]
    public async Task A_done_deadline_and_a_deadline_without_channels_are_not_reminded()
    {
        var scope = new ApplicationTestScope(Now);
        var done = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today), default);
        await scope.CompleteDeadline.ExecuteAsync(done, Today, default);
        await scope.SaveDeadline.ExecuteAsync(Plan(null, Today, channels: NotificationChannels.None), default);

        (await scope.DeadlineReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_a_device_that_sends_email_emails_the_reminder()
    {
        var scope = new ApplicationTestScope(Now);
        await scope.SaveDeadline.ExecuteAsync(Plan(null, Today), default);

        await scope.DeadlineReminders.RunAsync(Today, sendsEmail: false, default);

        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_reminder_is_attempted_again()
    {
        var scope = new ApplicationTestScope(Now);
        await scope.SaveDeadline.ExecuteAsync(Plan(null, Today, channels: NotificationChannels.Windows), default);
        scope.Windows.ShouldFail = true;

        (await scope.DeadlineReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(0);
        scope.Windows.ShouldFail = false;
        (await scope.DeadlineReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(1);
    }

    [Fact]
    public async Task The_periodic_check_runs_the_reminders()
    {
        var scope = new ApplicationTestScope(Now);
        await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(1), channels: NotificationChannels.Windows), default);

        await scope.Monitor.RunAsync(default);

        scope.DeadlineReminderEvents.All.Should().ContainSingle();
        scope.Windows.Targets.Should().ContainSingle()
            .Which.Should().Be(NotificationTarget.Deadline(null));
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_deadlines_only()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        await scope.SaveDeadline.ExecuteAsync(Plan(medicine, Today.AddDays(29)), default);
        var own = await scope.SaveDeadline.ExecuteAsync(Plan(null, Today.AddDays(29)), default);

        await scope.Deletion.RemoveAsync(medicine, default);

        scope.Deadlines.All.Should().ContainSingle().Which.Id.Should().Be(own);
    }
}
