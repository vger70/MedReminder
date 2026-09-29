using FluentAssertions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Stock;
using Xunit;

namespace MedReminder.Application.Tests.Monitoring;

public class MedicationMonitorTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    // Convenience seed: creates a medicine with StartDate = "today" to avoid
    // che il consumo giornaliero materializzato dal catch-up alteri lo
    // scenario. The monitor tested here does NOT invoke the catch-up: that is
    // testato in ConsumptionCatchUpTests.
    private static async Task<Guid> SeedAsync(
        ApplicationTestScope scope,
        decimal initialQuantity,
        int thresholdDays = 7,
        NotificationChannels channels = NotificationChannels.Windows,
        DateOnly? endDate = null)
    {
        var cmd = new AddMedicineCommand(
            Name: "Enalapril",
            Unit: "compresse",
            DosePerAdministration: 1m,
            AdministrationsPerDay: 2,
            StartDate: new DateOnly(2026, 9, 13),
            ThresholdDays: thresholdDays,
            NotificationChannels: channels,
            EndDate: endDate,
            InitialQuantity: initialQuantity);
        return await scope.AddMedicine.ExecuteAsync(cmd, CancellationToken.None);
    }

    [Fact]
    public async Task Sends_windows_notification_when_days_remaining_within_threshold()
    {
        var scope = new ApplicationTestScope(FixedNow);
        // Stock 6, rate 2 → 3 days remaining < threshold 7.
        var id = await SeedAsync(scope, initialQuantity: 6m);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        result.NotificationsSent.Should().Be(1);
        scope.Windows.Sent.Should().ContainSingle();
        scope.Email.Sent.Should().BeEmpty();

        var events = scope.Notifications.All;
        events.Should().ContainSingle();
        events[0].StockEpoch.Should().Be(1);
        events[0].Success.Should().BeTrue();
        events[0].DaysRemainingAtSend.Should().Be(3);
        events[0].MedicineId.Should().Be(id);
    }

    // Household step H1: a successful email is recorded as a replicated
    // fact, and an operation while sync is enabled.
    [Fact]
    public async Task A_sent_email_is_recorded_for_the_epoch_and_as_an_operation()
    {
        var scope = new ApplicationTestScope(FixedNow);
        scope.EnableSync();
        var id = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Windows | NotificationChannels.Email);

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Email.Sent.Should().ContainSingle();
        var sent = scope.SentEmails.All.Should().ContainSingle().Subject;
        sent.MedicineId.Should().Be(id);
        sent.StockEpoch.Should().Be(1);
        scope.SyncOperations.All.Should().ContainSingle(o => o.Type == "EmailNotificationSent" && o.EntityId == sent.Id);
    }

    [Fact]
    public async Task A_failed_email_is_not_recorded_as_sent()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m, channels: NotificationChannels.Email);
        scope.Email.ShouldFail = true;

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.SentEmails.All.Should().BeEmpty();
    }

    // Another device of the group already emailed for this epoch: this
    // device shows its own toast and does not email again.
    [Fact]
    public async Task An_email_sent_elsewhere_for_the_epoch_is_not_sent_again_but_the_toast_is_shown()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Windows | NotificationChannels.Email);
        var medicine = (await scope.Medicines.GetAsync(id, CancellationToken.None))!;
        await scope.SentEmails.AddAsync(new SentEmailNotification
        {
            MedicineId = id,
            StockEpoch = medicine.StockEpoch,
            EpochFactId = medicine.StockEpochFactId,
            SentAt = FixedNow.AddMinutes(-5),
        }, CancellationToken.None);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Email.Sent.Should().BeEmpty();
        scope.Windows.Sent.Should().ContainSingle();
        result.NotificationsSent.Should().Be(1);
        scope.Notifications.All.Should().ContainSingle().Which.Success.Should().BeTrue();
        scope.SentEmails.All.Should().ContainSingle();
    }

    [Fact]
    public async Task An_email_sent_elsewhere_for_an_older_epoch_does_not_stop_the_email()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m, channels: NotificationChannels.Email);
        var medicine = (await scope.Medicines.GetAsync(id, CancellationToken.None))!;
        await scope.SentEmails.AddAsync(new SentEmailNotification
        {
            MedicineId = id,
            StockEpoch = medicine.StockEpoch - 1,
            EpochFactId = Guid.NewGuid(),
            SentAt = FixedNow.AddDays(-20),
        }, CancellationToken.None);

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Email.Sent.Should().ContainSingle();
    }

    // Email only, already sent elsewhere: nothing is dispatched, and the
    // epoch is closed on this device too.
    [Fact]
    public async Task An_email_only_medicine_already_emailed_elsewhere_closes_the_cycle_without_sending()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m, channels: NotificationChannels.Email);
        var medicine = (await scope.Medicines.GetAsync(id, CancellationToken.None))!;
        await scope.SentEmails.AddAsync(new SentEmailNotification
        {
            MedicineId = id,
            StockEpoch = medicine.StockEpoch,
            EpochFactId = medicine.StockEpochFactId,
            SentAt = FixedNow.AddMinutes(-5),
        }, CancellationToken.None);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);
        await scope.Monitor.RunAsync(CancellationToken.None);

        result.NotificationsSent.Should().Be(0);
        scope.Email.Sent.Should().BeEmpty();
        scope.Notifications.All.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Does_not_send_when_days_remaining_above_threshold()
    {
        var scope = new ApplicationTestScope(FixedNow);
        // Stock 30, rate 2 → 15 giorni > 7.
        _ = await SeedAsync(scope, initialQuantity: 30m);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        result.NotificationsSent.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
        scope.Notifications.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Second_run_within_same_epoch_does_not_duplicate_notification()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m);

        await scope.Monitor.RunAsync(CancellationToken.None);
        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Windows.Sent.Should().HaveCount(1);
        scope.Notifications.All.Should().HaveCount(1);
    }

    [Fact]
    public async Task New_stock_epoch_allows_next_notification()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m);

        await scope.Monitor.RunAsync(CancellationToken.None);

        // Refill: new epoch. Then we drop back under the threshold
        // with a negative correction that does not increment the epoch.
        await scope.AddStock.ExecuteAsync(
            new AddStockCommand(id, 30m, StockMovementKind.NewPackage),
            CancellationToken.None);
        await scope.AdjustStockDown.ExecuteAsync(
            new AdjustStockDownCommand(id, 32m),
            CancellationToken.None);  // 6+30-32 = 4 → 2 days remaining

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Windows.Sent.Should().HaveCount(2);
        scope.Notifications.All.Should().HaveCount(2);
        scope.Notifications.All.Select(e => e.StockEpoch).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Suspended_medicine_is_not_notified()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m);
        await scope.SuspendMedication.ExecuteAsync(
            new SuspendMedicationCommand(id, new DateOnly(2026, 9, 13)),
            CancellationToken.None);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        result.NotificationsSent.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task End_date_before_eta_suppresses_notification()
    {
        var scope = new ApplicationTestScope(FixedNow);
        // Stock 6, rate 2 → ETA 2026-09-16. EndDate 2026-09-14 (before).
        _ = await SeedAsync(scope, initialQuantity: 6m,
            endDate: new DateOnly(2026, 9, 14));

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        result.NotificationsSent.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatches_to_both_channels_when_configured()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Both);

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Windows.Sent.Should().HaveCount(1);
        scope.Email.Sent.Should().HaveCount(1);
        scope.Notifications.All.Single().Channel.Should().Be(NotificationChannels.Both);
        scope.Notifications.All.Single().Success.Should().BeTrue();
    }

    [Fact]
    public async Task Email_failure_does_not_prevent_windows_success()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Both);
        scope.Email.ShouldFail = true;

        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Windows.Sent.Should().HaveCount(1);
        scope.Email.Sent.Should().BeEmpty();

        var evt = scope.Notifications.All.Single();
        evt.Success.Should().BeTrue();      // almeno un canale ha funzionato
        evt.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task Both_channels_failing_records_unsuccessful_event()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Both);
        scope.Email.ShouldFail = true;
        scope.Windows.ShouldFail = true;

        await scope.Monitor.RunAsync(CancellationToken.None);

        var evt = scope.Notifications.All.Single();
        evt.Success.Should().BeFalse();
        evt.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task Failed_event_does_not_suppress_next_run_retry()
    {
        var scope = new ApplicationTestScope(FixedNow);
        _ = await SeedAsync(scope, initialQuantity: 6m,
            channels: NotificationChannels.Windows);
        scope.Windows.ShouldFail = true;

        await scope.Monitor.RunAsync(CancellationToken.None);

        // Prossimo giro l'ambiente si è ripreso.
        scope.Windows.ShouldFail = false;
        await scope.Monitor.RunAsync(CancellationToken.None);

        scope.Windows.Sent.Should().HaveCount(1);
        scope.Notifications.All.Should().HaveCount(2);
        scope.Notifications.All.Last().Success.Should().BeTrue();
    }

    [Fact]
    public async Task Inactive_medicine_is_skipped()
    {
        var scope = new ApplicationTestScope(FixedNow);
        var id = await SeedAsync(scope, initialQuantity: 6m);
        await scope.UpdateMedicine.ExecuteAsync(
            new UpdateMedicineCommand(
                MedicineId: id,
                Name: "Enalapril",
                ActiveIngredient: null,
                Package: null,
                Unit: "compresse",
                ThresholdDays: 7,
                NotificationChannels: NotificationChannels.Windows,
                EndDate: null,
                DoctorName: null,
                Notes: null,
                IsActive: false),
            CancellationToken.None);

        var result = await scope.Monitor.RunAsync(CancellationToken.None);

        result.MedicinesInspected.Should().Be(0);
        scope.Windows.Sent.Should().BeEmpty();
    }
}
