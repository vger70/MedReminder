using System.Globalization;
using MedReminder.Application.Donations;

namespace MedReminder.Infrastructure.Donations;

// Shared base for the Payment-Link providers (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.2). Stripe and PayPal differ
// only by which config section (ProviderOptions) they read and by
// their display name — the resolve logic is identical: a pure lookup
// over the bound PaymentLinks map, with URL parse / HTTPS validation.
//
// Contains no provider SDK, no HttpClient and no secret. The amount is
// used only to select a pre-made link; it is never injected into the
// URL. The custom link is returned verbatim.
public abstract class PaymentLinkDonationProvider : IDonationProvider
{
    // The reserved config key for the provider-native "choose your
    // amount" link.
    private const string CustomKey = "custom";

    private readonly ProviderOptions _options;

    protected PaymentLinkDonationProvider(ProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public abstract DonationProvider Kind { get; }

    public abstract string Name { get; }

    // Presence-based: the custom option is offered whenever a non-empty
    // "custom" entry exists. URL shape is validated at resolve time, so
    // a misconfigured (non-HTTPS / malformed) custom link still surfaces
    // its specific failure reason instead of silently hiding the option.
    public bool SupportsCustomAmount =>
        _options.PaymentLinks is not null &&
        _options.PaymentLinks.TryGetValue(CustomKey, out var url) &&
        !string.IsNullOrWhiteSpace(url);

    public DonationLaunchResult Resolve(decimal amount)
    {
        var key = ((int)amount).ToString(CultureInfo.InvariantCulture);
        if (_options.PaymentLinks is null ||
            !_options.PaymentLinks.TryGetValue(key, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.MissingUrl);
        }
        return Validate(raw);
    }

    public DonationLaunchResult ResolveCustom()
    {
        if (_options.PaymentLinks is null ||
            !_options.PaymentLinks.TryGetValue(CustomKey, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.CustomAmountUnavailable);
        }
        // Opened verbatim — no amount appended.
        return Validate(raw);
    }

    // Absolute-URI + HTTPS validation. The single point where a
    // configured string becomes a launchable Uri.
    private static DonationLaunchResult Validate(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.MalformedUrl);
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return DonationMessageKeys.Failure(DonationFailureReason.NotHttps);
        }
        return DonationLaunchResult.Ok(uri, DonationMessageKeys.Launched);
    }
}
