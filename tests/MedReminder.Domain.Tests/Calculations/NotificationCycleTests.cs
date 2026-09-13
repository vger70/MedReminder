using FluentAssertions;
using MedReminder.Domain.Calculations;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Tests.Support;
using Xunit;

namespace MedReminder.Domain.Tests.Calculations;

public class NotificationCycleTests
{
    private static readonly DateOnly Today = new(2026, 9, 13);

    [Fact]
    public void Inactive_medicine_never_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, isActive: false);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 3, estimatedRunOutDate: Today.AddDays(3),
            successfulNotificationForCurrentEpoch: null)
            .Should().BeFalse();
    }

    [Fact]
    public void No_channels_configured_suppresses_notification()
    {
        var medicine = DomainFactory.Medicine(channels: NotificationChannels.None, thresholdDays: 7);

        NotificationCycle.ShouldNotify(medicine, 3, Today.AddDays(3), null)
            .Should().BeFalse();
    }

    [Fact]
    public void Null_days_remaining_does_not_notify()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: null, estimatedRunOutDate: null,
            successfulNotificationForCurrentEpoch: null)
            .Should().BeFalse();
    }

    [Fact]
    public void Above_threshold_does_not_notify()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 8, estimatedRunOutDate: Today.AddDays(8), null)
            .Should().BeFalse();
    }

    [Fact]
    public void At_threshold_boundary_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 7, estimatedRunOutDate: Today.AddDays(7), null)
            .Should().BeTrue();
    }

    [Fact]
    public void Within_threshold_no_history_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 3, estimatedRunOutDate: Today.AddDays(3), null)
            .Should().BeTrue();
    }

    [Fact]
    public void Successful_notification_for_current_epoch_suppresses_reissue()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);
        var evt = DomainFactory.NotificationEventForEpoch(epoch: 5, success: true);

        NotificationCycle.ShouldNotify(medicine, 3, Today.AddDays(3), evt)
            .Should().BeFalse();
    }

    [Fact]
    public void Failed_notification_does_not_block_next_attempt()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);
        var evt = DomainFactory.NotificationEventForEpoch(epoch: 5, success: false);

        NotificationCycle.ShouldNotify(medicine, 3, Today.AddDays(3), evt)
            .Should().BeTrue();
    }

    [Fact]
    public void Notification_for_previous_epoch_does_not_suppress_new_cycle()
    {
        // After a stock refill the epoch advances (e.g. 5 -> 6). A
        // successful notification recorded on epoch 5 must NOT prevent a
        // fresh notification on epoch 6 when back below threshold.
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 6);
        var evt = DomainFactory.NotificationEventForEpoch(epoch: 5, success: true);

        NotificationCycle.ShouldNotify(medicine, 3, Today.AddDays(3), evt)
            .Should().BeTrue();
    }

    [Fact]
    public void Eta_beyond_end_of_therapy_suppresses_notification()
    {
        // La terapia termina il 20/09; l'ETA (23/09) è successiva. Nessun
        // avviso: non serve una nuova prescrizione.
        var medicine = DomainFactory.Medicine(thresholdDays: 30, endDate: new DateOnly(2026, 9, 20));
        var eta = new DateOnly(2026, 9, 23);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 10, estimatedRunOutDate: eta, null)
            .Should().BeFalse();
    }

    [Fact]
    public void Eta_on_or_before_end_of_therapy_still_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 30, endDate: new DateOnly(2026, 9, 25));
        var eta = new DateOnly(2026, 9, 20);

        NotificationCycle.ShouldNotify(medicine, 7, eta, null).Should().BeTrue();
    }

    [Fact]
    public void Threshold_zero_and_days_zero_still_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 0);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 0, estimatedRunOutDate: Today, null)
            .Should().BeTrue();
    }
}
