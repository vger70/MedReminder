using FluentAssertions;
using MedReminder.Application.Notifications;
using MedReminder.Application.Tests.Support;
using MedReminder.Domain.Medicines;
using Xunit;

namespace MedReminder.Application.Tests.Notifications;

// The dosage lines of the low-stock email
// (ANALYSIS-INTRADAY-CONSUMPTION.md §5.1): an as-needed slot is marked in
// the email's language, once.
public class NotificationTextsTests
{
    private static readonly Medicine Medicine = new() { Name = "Paracetamolo", Unit = "compresse" };

    private static MedicationAdministrationSlot Slot(string? label, bool asNeeded)
        => new() { MedicineId = Medicine.Id, Dose = 1m, TimingLabel = label, IsAsNeeded = asNeeded };

    [Fact]
    public void An_as_needed_slot_is_marked_in_the_email_language()
    {
        var email = NotificationTexts.BuildEmail(Medicine, 5m, 5, null,
            administrationSlots: [Slot("Dopo cena", asNeeded: true)],
            localization: new JsonDictionaryLocalizationService("it"));

        email.Body.Should().Contain("1 compresse Dopo cena (al bisogno)");
        email.Body.Should().NotContain("as needed");
    }

    [Fact]
    public void The_marker_is_not_repeated_after_the_as_needed_description()
    {
        var email = NotificationTexts.BuildEmail(Medicine, 5m, 5, null,
            administrationSlots: [Slot("Al bisogno", asNeeded: true), Slot("Al mattino", asNeeded: false)],
            localization: new JsonDictionaryLocalizationService("it"));

        email.Body.Should().Contain("1 compresse Al bisogno\n");
        email.Body.Should().Contain("1 compresse Al mattino\n");
        email.Body.Should().NotContain("(al bisogno)");
    }
}
