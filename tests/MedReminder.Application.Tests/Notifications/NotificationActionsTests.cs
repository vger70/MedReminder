using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using Xunit;

namespace MedReminder.Application.Tests.Notifications;

// Actions on Windows notifications (docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.4): the toast arguments and the targets the notifiers pass.
public class NotificationActionsTests
{
    private static readonly Guid Medicine = Guid.Parse("0b0b0b0b-0000-0000-0000-000000000001");
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Every_action_round_trips_through_the_toast_arguments()
    {
        var actions = new[]
        {
            new NotificationAction(NotificationActionKind.Open, "p1", Medicine),
            new NotificationAction(NotificationActionKind.OpenPrescriptions, "p1", Medicine),
            new NotificationAction(NotificationActionKind.OpenDeadlines, "p1", Guid.Empty),
            new NotificationAction(NotificationActionKind.Snooze, "p1", Medicine, new TimeOnly(8, 30)),
            new NotificationAction(NotificationActionKind.RequestPrescription, "p1", Medicine),
        };

        foreach (var action in actions)
        {
            NotificationActionArguments.Parse(NotificationActionArguments.Format(action)).Should().Be(action);
        }
    }

    [Fact]
    public void The_arguments_carry_identifiers_only()
    {
        var args = NotificationActionArguments.Format(
            new NotificationAction(NotificationActionKind.Snooze, "p1", Medicine, new TimeOnly(8, 30)));

        args.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["action"] = "snooze",
            ["profile"] = "p1",
            ["medicine"] = "0b0b0b0b000000000000000000000001",
            ["slot"] = "08:30",
        });
    }

    [Fact]
    public void Arguments_this_version_does_not_understand_give_no_action()
    {
        Dictionary<string, string> Args(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

        NotificationActionArguments.Parse(Args()).Should().BeNull();
        NotificationActionArguments.Parse(Args(("action", "take"), ("profile", "p"), ("medicine", Medicine.ToString("N"))))
            .Should().BeNull();
        NotificationActionArguments.Parse(Args(("action", "open"), ("medicine", Medicine.ToString("N"))))
            .Should().BeNull();
        NotificationActionArguments.Parse(Args(("action", "open"), ("profile", "p"), ("medicine", "x")))
            .Should().BeNull();
        NotificationActionArguments.Parse(Args(("action", "snooze"), ("profile", "p"), ("medicine", Medicine.ToString("N"))))
            .Should().BeNull("a snooze needs its slot");
    }

    [Fact]
    public void Each_kind_of_notification_offers_its_actions()
    {
        NotificationActionArguments.ButtonsFor(NotificationTarget.DoseReminder(Medicine, new TimeOnly(8, 0)), "p")
            .Should().ContainSingle().Which.Kind.Should().Be(NotificationActionKind.Snooze);
        NotificationActionArguments.ButtonsFor(NotificationTarget.LowStock(Medicine), "p")
            .Should().ContainSingle().Which.Kind.Should().Be(NotificationActionKind.RequestPrescription);
        NotificationActionArguments.ButtonsFor(NotificationTarget.Prescription(Medicine), "p").Should().BeEmpty();

        NotificationActionArguments.Parse(NotificationActionArguments.ForBody(NotificationTarget.LowStock(Medicine), "p"))!
            .Kind.Should().Be(NotificationActionKind.Open);
        NotificationActionArguments.Parse(NotificationActionArguments.ForBody(NotificationTarget.Prescription(Medicine), "p"))!
            .Kind.Should().Be(NotificationActionKind.OpenPrescriptions);
        NotificationActionArguments.ButtonsFor(NotificationTarget.Deadline(null), "p").Should().BeEmpty();
        NotificationActionArguments.Parse(NotificationActionArguments.ForBody(NotificationTarget.Deadline(null), "p"))!
            .Should().Be(new NotificationAction(NotificationActionKind.OpenDeadlines, "p", Guid.Empty));
    }

    [Fact]
    public void A_notice_key_names_one_notice_whatever_the_day()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        NoticeKeys.PackageExpiry(1, [(a, Today), (b, Today)]).Should().Be(NoticeKeys.PackageExpiry(1, [(b, Today), (a, Today)]));
        NoticeKeys.PackageExpiry(1, [(a, Today)]).Should().NotBe(NoticeKeys.PackageExpiry(2, [(a, Today)]));
        // A package whose expiry was corrected is a new notice.
        NoticeKeys.PackageExpiry(1, [(a, Today)]).Should().NotBe(NoticeKeys.PackageExpiry(1, [(a, Today.AddDays(3))]));
        // A new stock epoch (a refill) starts a new cycle: its stages are new notices.
        NoticeKeys.LowStock(a, stockEpoch: 1, epochFactId: null, stage: 1)
            .Should().NotBe(NoticeKeys.LowStock(a, stockEpoch: 2, epochFactId: null, stage: 1));
        NoticeKeys.LowStock(a, stockEpoch: 1, epochFactId: null, stage: 1)
            .Should().NotBe(NoticeKeys.LowStock(a, stockEpoch: 1, epochFactId: null, stage: 2));
        // The same number for another epoch (after a retraction).
        NoticeKeys.LowStock(a, stockEpoch: 4, epochFactId: Guid.NewGuid(), stage: 1)
            .Should().NotBe(NoticeKeys.LowStock(a, stockEpoch: 4, epochFactId: Guid.NewGuid(), stage: 1));
        NoticeKeys.Deadline(a, Today).Should().NotBe(NoticeKeys.Deadline(a, Today.AddDays(1)));
        NotificationTarget.DoseReminder(a, new TimeOnly(8, 0)).Notice.Should().BeNull();
    }

    // Review of #308: the packages of one expiry notice may have come due
    // on different days, each delivered on its own.
    [Fact]
    public void Delivered_notices_cover_a_notice_item_by_item()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var ab = NoticeKeys.PackageExpiry(1, [(a, Today), (b, Today)]);
        var cOnly = NoticeKeys.PackageExpiry(1, [(c, Today)]);
        var abc = NoticeKeys.PackageExpiry(1, [(a, Today), (b, Today), (c, Today)]);

        NoticeKeys.Covers([ab, cOnly], abc).Should().BeTrue();
        NoticeKeys.Covers([ab], abc).Should().BeFalse();
        // Another stage covers nothing.
        NoticeKeys.Covers([NoticeKeys.PackageExpiry(2, [(a, Today), (b, Today), (c, Today)])], abc).Should().BeFalse();
        // One item: the same key only.
        var stock = NoticeKeys.LowStock(a, stockEpoch: 1, epochFactId: null, stage: 1);
        NoticeKeys.Covers([stock], stock).Should().BeTrue();
        NoticeKeys.Covers([NoticeKeys.LowStock(a, stockEpoch: 1, epochFactId: null, stage: 2)], stock).Should().BeFalse();
        NoticeKeys.Covers([], stock).Should().BeFalse();
    }

    [Fact]
    public async Task The_low_stock_warning_and_the_prescription_reminder_carry_their_target()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        // Stock 6, rate 2: 3 days, inside the threshold.
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 2,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 6m), default);
        await scope.SavePrescription.ExecuteAsync(
            new SavePrescriptionCommand(null, id, null, Today, null, null, Today, null), default);

        await scope.Monitor.RunAsync(default);

        scope.Windows.Targets.Should().OnlyContain(t => t.Notice != null);
        scope.Windows.Targets.Select(t => t with { Notice = null }).Should().BeEquivalentTo(new[]
        {
            NotificationTarget.LowStock(id),
            NotificationTarget.Prescription(id),
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_language_names_the_buttons(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);

        loc.Get("Notifications.Action.Snooze", NotificationActionArguments.SnoozeMinutes).Should().Contain("15");
        loc.Get("Notifications.Action.RequestPrescription");

        loc.FallbackHits.Should().BeEmpty();
    }
}
