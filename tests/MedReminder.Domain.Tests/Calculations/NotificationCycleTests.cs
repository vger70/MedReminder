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
    public void An_email_of_the_same_epoch_fact_was_already_sent()
    {
        var medicine = DomainFactory.Medicine(stockEpoch: 2);
        var fact = Guid.NewGuid();
        medicine.StockEpochFactId = fact;

        NotificationCycle.EmailAlreadySent(medicine, Sent(epoch: 2, fact)).Should().BeTrue();
        // Epoch numbers can be reused after a retraction: the fact decides.
        NotificationCycle.EmailAlreadySent(medicine, Sent(epoch: 2, Guid.NewGuid())).Should().BeFalse();
        NotificationCycle.EmailAlreadySent(medicine, null).Should().BeFalse();
    }

    [Fact]
    public void Without_epoch_facts_the_email_epoch_number_decides()
    {
        var medicine = DomainFactory.Medicine(stockEpoch: 3);

        NotificationCycle.EmailAlreadySent(medicine, Sent(epoch: 3, null)).Should().BeTrue();
        NotificationCycle.EmailAlreadySent(medicine, Sent(epoch: 2, null)).Should().BeFalse();
    }

    private static SentEmailNotification Sent(int epoch, Guid? fact) => new()
    {
        MedicineId = DomainFactory.MedicineId,
        StockEpoch = epoch,
        EpochFactId = fact,
        SentAt = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Inactive_medicine_never_notifies()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, isActive: false);

        NotificationCycle.ShouldNotify(medicine, daysRemaining: 3, estimatedRunOutDate: Today.AddDays(3),
            latestNotificationForMedicine: null)
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
            latestNotificationForMedicine: null)
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

        NotificationCycle.ShouldNotify(medicine, 5, Today.AddDays(5), evt)
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
        // The therapy ends on Sep 20; the ETA (Sep 23) is later.
        // No warning: no new prescription is needed.
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

    // Second low-stock warning (docs/notes/EVOLUTION-PROPOSALS-2.md §3.1).

    [Theory]
    [InlineData(14, 7)]
    [InlineData(7, 3)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void The_second_warning_is_due_at_half_of_the_threshold(int threshold, int expected)
    {
        NotificationCycle.SecondWarningDays(threshold).Should().Be(expected);
    }

    [Theory]
    [InlineData(7, 7, 1)]
    [InlineData(7, 4, 1)]
    [InlineData(7, 3, 2)]
    [InlineData(7, 0, 2)]
    [InlineData(7, -2, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(0, 0, 1)]
    public void The_stage_follows_the_days_remaining(int threshold, int days, int expected)
    {
        NotificationCycle.StageFor(threshold, days).Should().Be(expected);
    }

    [Fact]
    public void A_first_warning_does_not_cover_the_second()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);
        var first = DomainFactory.NotificationEventForEpoch(epoch: 5, success: true, stage: 1);

        NotificationCycle.StageToNotify(medicine, 5, Today.AddDays(5), first).Should().BeNull();
        NotificationCycle.StageToNotify(medicine, 3, Today.AddDays(3), first).Should().Be(2);
    }

    [Fact]
    public void A_second_warning_closes_the_epoch()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);
        var second = DomainFactory.NotificationEventForEpoch(epoch: 5, success: true, stage: 2);

        NotificationCycle.StageToNotify(medicine, 1, Today.AddDays(1), second).Should().BeNull();
        // Days going back up without a refill (a smaller dose) do not
        // bring the first warning back.
        NotificationCycle.StageToNotify(medicine, 6, Today.AddDays(6), second).Should().BeNull();
    }

    [Fact]
    public void A_medicine_entering_the_window_below_half_gets_only_the_second_warning()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);

        NotificationCycle.StageToNotify(medicine, 2, Today.AddDays(2), null).Should().Be(2);
    }

    [Fact]
    public void A_failed_second_warning_is_attempted_again()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 5);
        var failed = DomainFactory.NotificationEventForEpoch(epoch: 5, success: false, stage: 2);

        NotificationCycle.StageToNotify(medicine, 3, Today.AddDays(3), failed).Should().Be(2);
    }

    [Fact]
    public void A_refill_restarts_from_the_first_warning()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, stockEpoch: 6);
        var previous = DomainFactory.NotificationEventForEpoch(epoch: 5, success: true, stage: 2);

        NotificationCycle.StageToNotify(medicine, 6, Today.AddDays(6), previous).Should().Be(1);
    }

    [Fact]
    public void The_therapy_end_rule_applies_to_the_second_warning()
    {
        var medicine = DomainFactory.Medicine(thresholdDays: 7, endDate: new DateOnly(2026, 9, 14));

        NotificationCycle.StageToNotify(medicine, 2, new DateOnly(2026, 9, 15), null).Should().BeNull();
    }

    [Fact]
    public void A_first_stage_email_does_not_cover_the_second_stage()
    {
        var medicine = DomainFactory.Medicine(stockEpoch: 3);
        var first = Sent(epoch: 3, null);
        var second = new SentEmailNotification
        {
            MedicineId = DomainFactory.MedicineId,
            StockEpoch = 3,
            SentAt = new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero),
            Stage = NotificationCycle.SecondStage,
        };

        NotificationCycle.EmailAlreadySent(medicine, first, NotificationCycle.FirstStage).Should().BeTrue();
        NotificationCycle.EmailAlreadySent(medicine, first, NotificationCycle.SecondStage).Should().BeFalse();
        NotificationCycle.EmailAlreadySent(medicine, second, NotificationCycle.SecondStage).Should().BeTrue();
        NotificationCycle.EmailAlreadySent(medicine, second, NotificationCycle.FirstStage).Should().BeTrue();
    }
}
