using FluentAssertions;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// Every language defines the texts of the prescription lifecycle.
public class PrescriptionTextsTests
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
        await scope.SavePrescription.ExecuteAsync(
            new SavePrescriptionCommand(null, medicine, null, Today, "NRE", null, Today, null), default);
        var reminders = new PrescriptionReminders(scope.Prescriptions, scope.PrescriptionReminderEvents,
            scope.Medicines, scope.Email, scope.Windows, scope.Clock, NullLogger<PrescriptionReminders>.Instance, loc);

        await reminders.RunAsync(Today, sendsEmail: true, default);
        foreach (var status in Enum.GetValues<PrescriptionStatus>()) loc.Get("Prescriptions.Status." + status);
        foreach (var error in Enum.GetValues<PrescriptionError>()) loc.Get("Ui.PrescriptionEditDialog.Error." + error);

        loc.FallbackHits.Should().BeEmpty();
        var (title, body) = scope.Windows.Sent.Should().ContainSingle().Subject;
        title.Should().Contain("Enalapril");
        body.Should().Contain(Today.ToString("d", loc.CurrentCulture)).And.NotContain("NRE");
    }
}
