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

    // Seed di comodo: crea una medicina con StartDate = "oggi" per evitare
    // che il consumo giornaliero materializzato dal catch-up alteri lo
    // scenario. Il monitor testato qui NON invoca il catch-up: quello è
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
        // Stock 6, rate 2 → 3 giorni residui < soglia 7.
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

        // Rifornimento: nuova epoch. Poi torniamo sotto soglia con una
        // correzione negativa che non incrementa l'epoch.
        await scope.AddStock.ExecuteAsync(
            new AddStockCommand(id, 30m, StockMovementKind.NewPackage),
            CancellationToken.None);
        await scope.AdjustStockDown.ExecuteAsync(
            new AdjustStockDownCommand(id, 32m),
            CancellationToken.None);  // 6+30-32 = 4 → 2 giorni residui

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
        // Stock 6, rate 2 → ETA 2026-09-16. EndDate 2026-09-14 (prima).
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
