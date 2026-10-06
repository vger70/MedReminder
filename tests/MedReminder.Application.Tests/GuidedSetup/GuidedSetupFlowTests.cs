using FluentAssertions;
using MedReminder.Application.GuidedSetup;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.GuidedSetup;

public class GuidedSetupFlowTests
{
    private static GuidedSetupFlow Flow(bool admin = true, bool smtp = true, bool sends = true,
        NewMedicineDefaults? defaults = null)
        => new(new GuidedSetupEnvironment(admin, smtp, sends), defaults);

    [Fact]
    public void Starts_on_the_audience_step_with_the_built_in_warning()
    {
        var flow = Flow();

        flow.Current.Should().Be(GuidedSetupStep.Audience);
        flow.CanGoBack.Should().BeFalse();
        flow.LeadDays.Should().Be(7);
        flow.Channels.Should().Be(NotificationChannels.Windows);
        flow.Outcome.Should().Be(GuidedSetupOutcome.None);
    }

    [Fact]
    public void Starts_from_the_stored_defaults_when_given()
    {
        var flow = Flow(defaults: new NewMedicineDefaults(14, NotificationChannels.Both));

        flow.LeadDays.Should().Be(14);
        flow.Channels.Should().Be(NotificationChannels.Both);
    }

    [Fact]
    public void For_me_with_windows_only_skips_the_email_step()
    {
        var flow = Flow();

        flow.Steps.Should().Equal(GuidedSetupStep.Audience, GuidedSetupStep.Medicines,
            GuidedSetupStep.Warning, GuidedSetupStep.Summary);
        flow.Next().Should().Be(GuidedSetupStep.Medicines);
        flow.Next().Should().Be(GuidedSetupStep.Warning);
        flow.Next().Should().Be(GuidedSetupStep.Summary);
        flow.IsLastStep.Should().BeTrue();
    }

    [Theory]
    [InlineData(NotificationChannels.Email)]
    [InlineData(NotificationChannels.Both)]
    public void An_email_channel_shows_the_email_step(NotificationChannels channels)
    {
        var flow = Flow();
        flow.Next();
        flow.Next();
        flow.Channels = channels;

        flow.Next().Should().Be(GuidedSetupStep.Email);
        flow.Next().Should().Be(GuidedSetupStep.Summary);
        flow.Steps.Should().HaveCount(5);
    }

    [Fact]
    public void Someone_i_look_after_shows_the_email_step_with_windows_only()
    {
        var flow = Flow();
        flow.Audience = GuidedSetupAudience.SomeoneElse;

        flow.ShowsEmailStep.Should().BeTrue();
        flow.Next();
        flow.Next();
        flow.Next().Should().Be(GuidedSetupStep.Email);
    }

    [Fact]
    public void Back_after_dropping_email_skips_the_email_step_again()
    {
        var flow = Flow();
        flow.Next();
        flow.Next();
        flow.Channels = NotificationChannels.Email;
        flow.Next().Should().Be(GuidedSetupStep.Email);

        flow.Back().Should().Be(GuidedSetupStep.Warning);
        flow.Channels = NotificationChannels.Windows;

        flow.Next().Should().Be(GuidedSetupStep.Summary);
        flow.Back().Should().Be(GuidedSetupStep.Warning);
    }

    [Fact]
    public void Step_indicator_follows_the_shown_steps()
    {
        var flow = Flow();
        flow.Next();
        flow.Next();
        flow.Next();

        flow.CurrentIndex.Should().Be(3);
        flow.Steps.Count.Should().Be(4);
    }

    [Fact]
    public void Every_step_can_be_skipped_up_to_the_summary_and_finished()
    {
        var flow = Flow();
        while (!flow.IsLastStep) flow.Next();

        flow.Finish();

        flow.Outcome.Should().Be(GuidedSetupOutcome.Completed);
        flow.MarksShown.Should().BeTrue();
        flow.AddedMedicines.Should().BeEmpty();
    }

    [Fact]
    public void Finish_is_refused_before_the_summary()
    {
        var flow = Flow();

        flow.Invoking(f => f.Finish()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Not_now_or_close_at_any_step_marks_it_shown(int stepsForward)
    {
        var flow = Flow();
        for (var i = 0; i < stepsForward; i++) flow.Next();

        flow.Dismiss();

        flow.Outcome.Should().Be(GuidedSetupOutcome.Dismissed);
        flow.MarksShown.Should().BeTrue();
        flow.Invoking(f => f.Next()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Closing_after_finish_stays_completed()
    {
        var flow = Flow();
        while (!flow.IsLastStep) flow.Next();
        flow.Finish();

        flow.Dismiss();

        flow.Outcome.Should().Be(GuidedSetupOutcome.Completed);
    }

    [Fact]
    public void An_open_flow_does_not_mark_it_shown()
        => Flow().MarksShown.Should().BeFalse();

    [Theory]
    [InlineData(true, true, true, EmailAccountState.Configured)]
    [InlineData(false, true, true, EmailAccountState.Configured)]
    [InlineData(true, false, true, EmailAccountState.AdministratorCanSetUp)]
    [InlineData(false, false, true, EmailAccountState.AdministratorRequired)]
    [InlineData(true, false, false, EmailAccountState.AdministratorCanSetUp)]
    public void Email_account_state_follows_role_and_smtp(bool admin, bool smtp, bool sends, EmailAccountState expected)
    {
        var flow = Flow(admin, smtp, sends);

        flow.EmailAccount.Should().Be(expected);
        flow.SendsEmail.Should().Be(sends);
    }

    [Fact]
    public void Smtp_configured_in_settings_is_read_again()
    {
        var flow = Flow(admin: true, smtp: false);

        flow.SetSmtpConfigured(true);

        flow.EmailAccount.Should().Be(EmailAccountState.Configured);
    }

    [Fact]
    public void For_me_the_user_address_is_the_recipient_and_the_other_the_caregiver()
    {
        var flow = Flow();

        flow.MapAddresses(" me@example.org ", "helper@example.org")
            .Should().Be(new GuidedSetupAddresses("me@example.org", "helper@example.org"));
    }

    [Fact]
    public void For_someone_i_look_after_the_user_address_is_the_caregiver()
    {
        var flow = Flow();
        flow.Audience = GuidedSetupAudience.SomeoneElse;

        flow.MapAddresses("carer@example.org", "patient@example.org")
            .Should().Be(new GuidedSetupAddresses("patient@example.org", "carer@example.org"));
        flow.AddressFields(new GuidedSetupAddresses("patient@example.org", "carer@example.org"))
            .Should().Be(("carer@example.org", "patient@example.org"));
    }

    // Without a ToAddress no warning is ever emailed: a caregiver alone
    // is the recipient.
    [Fact]
    public void A_caregiver_alone_receives_the_warnings()
    {
        var flow = Flow();
        flow.Audience = GuidedSetupAudience.SomeoneElse;

        flow.MapAddresses("carer@example.org", " ")
            .Should().Be(new GuidedSetupAddresses("carer@example.org", string.Empty));
        flow.AddressFields(new GuidedSetupAddresses("carer@example.org", string.Empty))
            .Should().Be(("carer@example.org", string.Empty));
    }

    [Theory]
    [InlineData(NotificationChannels.Email, true, true, false)]
    [InlineData(NotificationChannels.Email, false, true, true)]
    [InlineData(NotificationChannels.Email, true, false, true)]
    [InlineData(NotificationChannels.Both, false, false, false)]
    [InlineData(NotificationChannels.Windows, false, false, false)]
    public void Email_only_without_smtp_or_recipient_warns_no_one(
        NotificationChannels channels, bool smtp, bool hasRecipient, bool expected)
    {
        var flow = Flow(smtp: smtp);
        flow.Channels = channels;

        flow.WarnsNoOne(hasRecipient).Should().Be(expected);
    }

    [Fact]
    public void Also_warn_on_windows_adds_the_channel()
    {
        var flow = Flow(smtp: false);
        flow.Channels = NotificationChannels.Email;

        flow.AddWindowsChannel();

        flow.Channels.Should().Be(NotificationChannels.Both);
        flow.WarnsNoOne(hasRecipient: false).Should().BeFalse();
        flow.Defaults.Should().Be(new NewMedicineDefaults(7, NotificationChannels.Both));
    }

    [Fact]
    public void Channels_cannot_be_none_and_lead_time_stays_in_range()
    {
        var flow = Flow();

        flow.Invoking(f => f.Channels = NotificationChannels.None).Should().Throw<ArgumentOutOfRangeException>();
        flow.Invoking(f => f.LeadDays = -1).Should().Throw<ArgumentOutOfRangeException>();
        flow.Invoking(f => f.LeadDays = 366).Should().Throw<ArgumentOutOfRangeException>();
        flow.LeadDays = 30;
        flow.LeadDays.Should().Be(30);
    }

    [Fact]
    public void Added_medicines_keep_their_order_once_each()
    {
        var flow = Flow();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        flow.AddMedicine(a);
        flow.AddMedicine(b);
        flow.AddMedicine(a);

        flow.AddedMedicines.Should().Equal(a, b);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Opens_by_itself_only_for_an_empty_profile_not_shown_before(bool hasMedicines, bool shown, bool expected)
        => GuidedSetupFlow.OpensByItself(hasMedicines, shown).Should().Be(expected);

    [Theory]
    [InlineData(7, NotificationChannels.Windows, true)]
    [InlineData(0, NotificationChannels.Email, true)]
    [InlineData(365, NotificationChannels.Both, true)]
    [InlineData(-1, NotificationChannels.Windows, false)]
    [InlineData(366, NotificationChannels.Windows, false)]
    [InlineData(7, NotificationChannels.None, false)]
    [InlineData(7, (NotificationChannels)8, false)]
    public void New_medicine_defaults_accept_only_valid_values(int days, NotificationChannels channels, bool valid)
        => (NewMedicineDefaults.TryCreate(days, channels) is not null).Should().Be(valid);
}
