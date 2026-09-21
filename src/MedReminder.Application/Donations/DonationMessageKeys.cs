namespace MedReminder.Application.Donations;

// Single source of truth mapping donation outcomes to localization
// keys (A6, docs/ANALYSIS-A6-DONATION-SUPPORT.md §11). Results carry a
// key, never rendered text, so the UI stays the one place that
// resolves strings. Both the service and the provider adapters use
// this helper so a reason and its user-facing key never drift apart.
public static class DonationMessageKeys
{
    // Shown after a successful browser launch. Deliberately claims
    // only that a page was opened — never that a payment completed
    // (§8.3).
    public const string Launched = "Ui.Donate.Launched";

    // Non-technical message key for a failure reason.
    public static string ForFailure(DonationFailureReason reason) => reason switch
    {
        DonationFailureReason.Disabled => "Ui.Donate.Error.Disabled",
        DonationFailureReason.ProviderDisabled => "Ui.Donate.Error.ProviderDisabled",
        DonationFailureReason.InvalidAmount => "Ui.Donate.Error.InvalidAmount",
        DonationFailureReason.UnsupportedAmount => "Ui.Donate.Error.UnsupportedAmount",
        DonationFailureReason.MissingUrl => "Ui.Donate.Error.MissingUrl",
        DonationFailureReason.MalformedUrl => "Ui.Donate.Error.MalformedUrl",
        DonationFailureReason.NotHttps => "Ui.Donate.Error.NotHttps",
        DonationFailureReason.LaunchFailed => "Ui.Donate.Error.LaunchFailed",
        DonationFailureReason.IncompleteConfig => "Ui.Donate.Error.IncompleteConfig",
        DonationFailureReason.CustomAmountUnavailable => "Ui.Donate.Error.CustomAmountUnavailable",
        _ => "Ui.Donate.Error.LaunchFailed",
    };

    // Failure result with the reason's key already attached.
    public static DonationLaunchResult Failure(DonationFailureReason reason) =>
        DonationLaunchResult.Fail(reason, ForFailure(reason));
}
