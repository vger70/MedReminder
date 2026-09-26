using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Prescriptions;

// Sends a prescription request the user has reviewed and explicitly
// confirmed in PrescriptionRequestDialog. Never called by the
// monitors or any scheduled path.
//
// The message carries health data and personal names (CLAUDE.md §7):
// the subject, the body and the recipient are never logged, and the
// exception is not attached to the log entry either, because SMTP
// error texts can echo the recipient address. Only the outcome and
// the exception type are recorded; the caller shows the exception
// message to the user.
public sealed class SendPrescriptionRequest
{
    private readonly IEmailNotificationService _email;
    private readonly ILogger<SendPrescriptionRequest> _log;

    public SendPrescriptionRequest(
        IEmailNotificationService email,
        ILogger<SendPrescriptionRequest> log)
    {
        _email = email;
        _log = log;
    }

    public async Task ExecuteAsync(
        string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        var message = new EmailMessage(subject, body, recipient.Trim());
        try
        {
            await _email.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.LogInformation("Prescription request cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Prescription request failed ({ExceptionType}).", ex.GetType().Name);
            throw;
        }

        _log.LogInformation("Prescription request sent.");
    }
}
