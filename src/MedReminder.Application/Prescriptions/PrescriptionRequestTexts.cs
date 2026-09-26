using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Medicines;

namespace MedReminder.Application.Prescriptions;

// Builds the draft of a prescription request addressed to the doctor
// (docs/notes/EVOLUTION-PROPOSALS.md §3.4). Pure: no I/O, no logging.
//
// Content is limited to what identifies the product (name, package,
// national code) plus the doctor's name in the greeting and the
// profile display name as signature. Dosage, stock, notes and any
// other clinical detail are deliberately left out: the user reviews
// and can edit the draft before it leaves the app.
//
// Language follows the UI language (ILocalizationService.Get), like
// the low-stock email built by NotificationTexts.BuildEmail.
public static class PrescriptionRequestTexts
{
    public static EmailMessage Build(
        Medicine medicine,
        string? profileDisplayName,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(medicine);
        ArgumentNullException.ThrowIfNull(localization);

        var subject = localization.Get("PrescriptionRequest.Subject", medicine.Name);

        var body = new StringBuilder();
        body.Append(string.IsNullOrWhiteSpace(medicine.DoctorName)
            ? localization.Get("PrescriptionRequest.Greeting")
            : localization.Get("PrescriptionRequest.Greeting.Named", medicine.DoctorName.Trim()));
        body.Append('\n').Append('\n');
        body.Append(localization.Get("PrescriptionRequest.Intro")).Append('\n').Append('\n');
        body.Append("  - ").Append(localization.Get("PrescriptionRequest.Medicine", medicine.Name)).Append('\n');
        if (!string.IsNullOrWhiteSpace(medicine.Package))
        {
            body.Append("  - ").Append(localization.Get("PrescriptionRequest.Package", medicine.Package.Trim())).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(medicine.NationalCode))
        {
            body.Append("  - ").Append(localization.Get("PrescriptionRequest.NationalCode", medicine.NationalCode.Trim())).Append('\n');
        }
        body.Append('\n');
        body.Append(localization.Get("PrescriptionRequest.Closing"));
        if (!string.IsNullOrWhiteSpace(profileDisplayName))
        {
            body.Append('\n').Append(profileDisplayName.Trim());
        }

        return new EmailMessage(subject, body.ToString());
    }
}
