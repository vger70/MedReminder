namespace MedReminder.Application.Donations;

// Every way a donation launch can fail (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §6). Each value maps to a
// single non-technical localization key (Ui.Donate.Error.*) so the
// dialog never surfaces a technical message; the technical detail
// only reaches the log.
public enum DonationFailureReason
{
    // The whole feature is off (DonationOptions.Enabled == false).
    Disabled,

    // The selected provider is off (ProviderOptions.Enabled == false).
    ProviderDisabled,

    // The requested amount is not a positive value.
    InvalidAmount,

    // The requested amount is positive but no tier link matches it.
    UnsupportedAmount,

    // The provider is enabled and has links, but the specific tier
    // (or "custom") key has no entry.
    MissingUrl,

    // The configured link does not parse as an absolute URI.
    MalformedUrl,

    // The configured link parses but is not HTTPS.
    NotHttps,

    // Process.Start / the browser hand-off threw.
    LaunchFailed,

    // The provider is enabled but its PaymentLinks map is empty/null.
    IncompleteConfig,

    // A custom amount was requested but the provider has no valid
    // "choose your amount" link configured.
    CustomAmountUnavailable,
}
