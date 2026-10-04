using FluentAssertions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Sync;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// Prescription lifecycle (docs/notes/EVOLUTION-PROPOSALS-2.md §3.2): save,
// collect, delete, list and the reminder to collect.
public class PrescriptionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static async Task<Guid> AddMedicineAsync(ApplicationTestScope scope, string name = "Enalapril",
        NotificationChannels channels = NotificationChannels.Windows | NotificationChannels.Email)
        => await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: channels, EndDate: null,
            InitialQuantity: 100m), default);

    private static SavePrescriptionCommand Issued(Guid medicineId, DateOnly until, Guid? id = null)
        => new(id, medicineId, Today.AddDays(-2), Today, " ABC123 ", 2, until, null);

    [Fact]
    public async Task A_saved_prescription_is_recorded_and_replicated_as_a_whole()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);

        var id = await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(29)), default);
        await scope.SavePrescription.ExecuteAsync(
            Issued(medicine, Today.AddDays(29), id) with { Packages = 3 }, default);

        var saved = scope.Prescriptions.All.Should().ContainSingle().Subject;
        saved.Code.Should().Be("ABC123");
        saved.Packages.Should().Be(3);
        var ops = scope.SyncOperations.All.Where(o => o.Type == nameof(PrescriptionChanged)).ToList();
        ops.Should().HaveCount(2).And.OnlyContain(o => o.SchemaVersion == 7 && o.EntityId == id);
        var winner = await scope.Registers.WinnerAsync(id, SyncRegisters.PrescriptionState, default);
        SyncRegisters.ParsePrescription(winner!.Value!).Packages.Should().Be(3);
    }

    [Fact]
    public async Task An_inconsistent_prescription_is_refused()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);

        var act = () => scope.SavePrescription.ExecuteAsync(
            Issued(medicine, Today.AddDays(-1)), default);

        (await act.Should().ThrowAsync<InvalidPrescriptionException>())
            .Which.Error.Should().Be(PrescriptionError.ValidBeforeIssued);
        scope.Prescriptions.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Collecting_keeps_the_rest_and_deleting_records_a_deletion()
    {
        var scope = new ApplicationTestScope(Now);
        scope.EnableSync();
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(29)), default);

        await scope.CollectPrescription.ExecuteAsync(id, Today, default);
        var collected = scope.Prescriptions.All.Should().ContainSingle().Subject;
        collected.CollectedOn.Should().Be(Today);
        collected.Code.Should().Be("ABC123");

        await scope.DeletePrescription.ExecuteAsync(id, default);
        scope.Prescriptions.All.Should().BeEmpty();
        var last = scope.SyncOperations.All.Last(o => o.Type == nameof(PrescriptionChanged));
        ((PrescriptionChanged)OperationCodec.Deserialize(last.Type, last.SchemaVersion, last.Payload))
            .Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task The_list_shows_the_prescriptions_to_act_on_first()
    {
        var scope = new ApplicationTestScope(Now);
        var a = await AddMedicineAsync(scope, "Aspirin");
        var b = await AddMedicineAsync(scope, "Bisoprolol");
        await scope.SavePrescription.ExecuteAsync(new(null, a, Today, null, null, null, null, null), default);
        await scope.SavePrescription.ExecuteAsync(
            new(null, a, null, Today.AddDays(-60), null, null, Today.AddDays(-31), Today.AddDays(-40)), default);
        var open = await scope.SavePrescription.ExecuteAsync(Issued(b, Today.AddDays(10)), default);
        await scope.SavePrescription.ExecuteAsync(
            new(null, b, null, Today.AddDays(-40), null, null, Today.AddDays(-11), null), default);

        var list = await scope.PrescriptionList.LoadAsync(default);

        list.Select(i => i.Status).Should().Equal(
            PrescriptionStatus.ToCollect, PrescriptionStatus.Requested, PrescriptionStatus.Expired,
            PrescriptionStatus.Collected);
        list[0].MedicineName.Should().Be("Bisoprolol");
        (await scope.PrescriptionList.OpenForMedicineAsync(b, default)).Select(o => o.Prescription.Id)
            .Should().HaveCount(2).And.EndWith(open);
        (await scope.PrescriptionList.OpenForMedicineAsync(a, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_reminder_to_collect_is_shown_once_per_end_date()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        var id = await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(5)), default);

        (await scope.PrescriptionReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(0, "too early");
        (await scope.PrescriptionReminders.RunAsync(Today.AddDays(2), sendsEmail: true, default)).Should().Be(1);
        (await scope.PrescriptionReminders.RunAsync(Today.AddDays(3), sendsEmail: true, default)).Should().Be(0);
        scope.Windows.Sent.Should().ContainSingle().Which.Body.Should().NotContain("ABC123");
        scope.Email.Sent.Should().ContainSingle();

        // A new end date gets its own reminder.
        await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(6), id), default);
        (await scope.PrescriptionReminders.RunAsync(Today.AddDays(4), sendsEmail: true, default)).Should().Be(1);
    }

    [Fact]
    public async Task Only_a_device_that_sends_email_emails_the_reminder()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today), default);

        await scope.PrescriptionReminders.RunAsync(Today, sendsEmail: false, default);

        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_reminder_is_attempted_again()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope, channels: NotificationChannels.Windows);
        await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today), default);
        scope.Windows.ShouldFail = true;

        (await scope.PrescriptionReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(0);
        scope.Windows.ShouldFail = false;
        (await scope.PrescriptionReminders.RunAsync(Today, sendsEmail: true, default)).Should().Be(1);
    }

    [Fact]
    public async Task The_periodic_check_runs_the_reminders()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope, channels: NotificationChannels.Windows);
        await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(1)), default);

        await scope.Monitor.RunAsync(default);

        scope.PrescriptionReminderEvents.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Deleting_the_medicine_removes_its_prescriptions()
    {
        var scope = new ApplicationTestScope(Now);
        var medicine = await AddMedicineAsync(scope);
        await scope.SavePrescription.ExecuteAsync(Issued(medicine, Today.AddDays(29)), default);

        await scope.Deletion.RemoveAsync(medicine, default);

        scope.Prescriptions.All.Should().BeEmpty();
    }
}
