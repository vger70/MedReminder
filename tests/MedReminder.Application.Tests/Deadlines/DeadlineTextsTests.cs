using FluentAssertions;
using MedReminder.Application.Deadlines;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Deadlines;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Deadlines;

// Every language defines the texts of the administrative deadlines.
public class DeadlineTextsTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public async Task The_reminder_and_the_labels_exist_in_every_language(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var medicine = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: "Enalapril", Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7, NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: 10m), default);
        await scope.SaveDeadline.ExecuteAsync(new SaveDeadlineCommand(
            null, medicine, DeadlineKind.TherapeuticPlan, null, Today.AddDays(3), 7, null,
            NotificationChannels.Windows, null), default);
        await scope.SaveDeadline.ExecuteAsync(new SaveDeadlineCommand(
            null, null, DeadlineKind.CheckUp, null, Today.AddDays(-2), 7, 12, NotificationChannels.Windows, null), default);
        var reminders = new DeadlineReminders(scope.Deadlines, scope.DeadlineReminderEvents, scope.Medicines,
            scope.Email, scope.Windows, scope.Clock, NullLogger<DeadlineReminders>.Instance, loc);

        await reminders.RunAsync(Today, sendsEmail: true, default);
        foreach (var kind in Enum.GetValues<DeadlineKind>()) DeadlineTexts.Kind(kind, loc);
        foreach (var status in Enum.GetValues<DeadlineStatus>()) loc.Get("Deadlines.Status." + status);
        foreach (var error in Enum.GetValues<DeadlineError>()) loc.Get("Ui.DeadlineEditDialog.Error." + error);

        loc.FallbackHits.Should().BeEmpty();
        scope.Windows.Sent.Should().HaveCount(2);
        var plan = scope.Windows.Sent.Single(s => s.Title.Contains("Enalapril"));
        plan.Body.Should().Contain(Today.AddDays(3).ToString("d", loc.CurrentCulture));
        plan.Title.Should().Contain(loc.Get("Deadlines.Kind.TherapeuticPlan"));
        scope.Windows.Sent.Should().ContainSingle(s => s.Body.Contains(Today.AddDays(-2).ToString("d", loc.CurrentCulture)));
    }

    [Fact]
    public void The_label_replaces_the_kind_and_the_medicine_follows_it()
    {
        var deadline = new Deadline { Kind = DeadlineKind.Other, Label = "Disability card", DueOn = Today };

        DeadlineTexts.Subject(deadline, null, null).Should().Be("Disability card");
        DeadlineTexts.Subject(deadline, "Enalapril", null).Should().Be("Disability card — Enalapril");
        DeadlineTexts.Subject(new Deadline { Kind = DeadlineKind.CheckUp, DueOn = Today }, null, null)
            .Should().Be("Check-up");
    }
}
