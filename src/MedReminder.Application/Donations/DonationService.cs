using System.Globalization;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Donations;

// The single orchestrator the UI talks to (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §6). The UI never touches a
// provider directly: it passes a DonationProvider enum value and an
// amount (or asks for the custom link), and this service runs the
// ordered validation pipeline, delegates the config lookup to the
// matching IDonationProvider, then launches the resolved public URL
// through IUrlLauncher.
//
// Ordering (fixed tier):
//   1. feature enabled            -> Disabled
//   2. provider enabled           -> ProviderDisabled
//   3. amount > 0                 -> InvalidAmount
//   4. amount <= max tier         -> UnsupportedAmount
//   5. PaymentLinks not empty     -> IncompleteConfig
//   6. provider resolves the tier -> MissingUrl / MalformedUrl / NotHttps
//   7. launch                     -> LaunchFailed / Success
//
// Ordering (custom):
//   1. feature enabled            -> Disabled
//   2. provider enabled           -> ProviderDisabled
//   3. custom link configured     -> CustomAmountUnavailable
//   4. provider resolves custom   -> MalformedUrl / NotHttps
//   5. launch                     -> LaunchFailed / Success
//
// No amount is EVER appended to any URL: fixed tiers open their own
// pre-made link and custom opens the provider-native link verbatim.
// The end user never types a URL (§6). No payment is ever claimed to
// have completed (§8.3) — success means only that a browser page was
// opened.
public sealed class DonationService
{
    private readonly DonationOptions _options;
    private readonly IReadOnlyDictionary<DonationProvider, IDonationProvider> _providers;
    private readonly IUrlLauncher _launcher;
    private readonly ILogger<DonationService> _log;

    public DonationService(
        DonationOptions options,
        IEnumerable<IDonationProvider> providers,
        IUrlLauncher launcher,
        ILogger<DonationService> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);
        _options = options;
        _providers = providers.ToDictionary(p => p.Kind);
        _launcher = launcher;
        _log = log;
    }

    // True when the feature is on and at least one provider is enabled
    // with a non-empty link map. The UI uses this to decide whether to
    // show/enable the menu entry (§8.1) and which provider radios to
    // offer.
    public bool IsFeatureAvailable =>
        _options.Enabled && (IsProviderUsable(DonationProvider.Stripe) || IsProviderUsable(DonationProvider.PayPal));

    // Whether a specific provider is enabled and has at least one link
    // configured. Drives which radio buttons the dialog shows.
    public bool IsProviderUsable(DonationProvider provider)
    {
        if (!_options.Enabled) return false;
        var opts = OptionsFor(provider);
        return opts is { Enabled: true, PaymentLinks.Count: > 0 };
    }

    // Whether the provider offers a custom ("choose your amount") link.
    public bool SupportsCustomAmount(DonationProvider provider) =>
        _providers.TryGetValue(provider, out var adapter) && adapter.SupportsCustomAmount;

    // Display name of a provider (falls back to the enum name if no
    // adapter is registered).
    public string GetProviderName(DonationProvider provider) =>
        _providers.TryGetValue(provider, out var adapter) ? adapter.Name : provider.ToString();

    // Fixed-tier donation. amount is one of the configured tiers
    // (€2/€5/€10/€20 in v1). The number is used only to pick the
    // pre-made link — it is never injected into the URL.
    public DonationLaunchResult Donate(DonationProvider provider, decimal amount)
    {
        if (!_options.Enabled)
        {
            return Reject(provider, DonationFailureReason.Disabled, amountLog: FormatAmount(amount));
        }

        var opts = OptionsFor(provider);
        if (opts is null || !opts.Enabled || !_providers.TryGetValue(provider, out var adapter))
        {
            return Reject(provider, DonationFailureReason.ProviderDisabled, amountLog: FormatAmount(amount));
        }

        // An enabled provider with no links at all is a configuration
        // gap, not an amount problem — report it before validating the
        // amount so the message points the maintainer at the config.
        if (opts.PaymentLinks is null || opts.PaymentLinks.Count == 0)
        {
            return Reject(provider, DonationFailureReason.IncompleteConfig, amountLog: FormatAmount(amount));
        }

        if (amount <= 0m)
        {
            return Reject(provider, DonationFailureReason.InvalidAmount, amountLog: FormatAmount(amount));
        }

        if (amount > MaxConfiguredTier(opts))
        {
            return Reject(provider, DonationFailureReason.UnsupportedAmount, amountLog: FormatAmount(amount));
        }

        var resolved = adapter.Resolve(amount);
        return Complete(provider, resolved, amountLog: FormatAmount(amount));
    }

    // Custom-amount donation. Opens the provider-native "choose your
    // amount" link verbatim; the amount is entered by the user on the
    // provider's hosted page. No number is passed by the app.
    public DonationLaunchResult DonateCustom(DonationProvider provider)
    {
        if (!_options.Enabled)
        {
            return Reject(provider, DonationFailureReason.Disabled, amountLog: "custom");
        }

        var opts = OptionsFor(provider);
        if (opts is null || !opts.Enabled || !_providers.TryGetValue(provider, out var adapter))
        {
            return Reject(provider, DonationFailureReason.ProviderDisabled, amountLog: "custom");
        }

        if (!adapter.SupportsCustomAmount)
        {
            return Reject(provider, DonationFailureReason.CustomAmountUnavailable, amountLog: "custom");
        }

        var resolved = adapter.ResolveCustom();
        return Complete(provider, resolved, amountLog: "custom");
    }

    // Shared tail: a resolve result from the provider is either a
    // failure (propagated with its key) or a validated HTTPS Uri to
    // launch.
    private DonationLaunchResult Complete(
        DonationProvider provider, DonationLaunchResult resolved, string amountLog)
    {
        if (!resolved.Success || resolved.ResolvedUrl is null)
        {
            var reason = resolved.Reason ?? DonationFailureReason.MissingUrl;
            _log.LogWarning(
                "Donation resolve failed for provider {Provider} amount {Amount}: {Reason}.",
                provider, amountLog, reason);
            // Normalise the message key in case the adapter did not set
            // one.
            return resolved.UserMessageKey is null
                ? DonationMessageKeys.Failure(reason)
                : resolved;
        }

        var url = resolved.ResolvedUrl;
        _log.LogInformation(
            "Attempting to open the donation checkout for provider {Provider} amount {Amount}.",
            provider, amountLog);

        if (_launcher.TryLaunch(url, out var error))
        {
            _log.LogInformation(
                "Donation checkout opened for provider {Provider} amount {Amount}.",
                provider, amountLog);
            return DonationLaunchResult.Ok(url, DonationMessageKeys.Launched);
        }

        _log.LogError(
            "Failed to open the donation checkout for provider {Provider} amount {Amount}: {Error}.",
            provider, amountLog, error);
        return DonationMessageKeys.Failure(DonationFailureReason.LaunchFailed);
    }

    private DonationLaunchResult Reject(
        DonationProvider provider, DonationFailureReason reason, string amountLog)
    {
        _log.LogWarning(
            "Donation rejected for provider {Provider} amount {Amount}: {Reason}.",
            provider, amountLog, reason);
        return DonationMessageKeys.Failure(reason);
    }

    private ProviderOptions? OptionsFor(DonationProvider provider) => provider switch
    {
        DonationProvider.Stripe => _options.Stripe,
        DonationProvider.PayPal => _options.PayPal,
        _ => null,
    };

    // Highest whole-euro tier configured (the "custom" key and any
    // non-integer key are ignored). Returns 0 when no numeric tier is
    // present, so any positive amount is then reported as
    // UnsupportedAmount rather than launching.
    private static decimal MaxConfiguredTier(ProviderOptions opts)
    {
        decimal max = 0m;
        if (opts.PaymentLinks is null) return max;
        foreach (var key in opts.PaymentLinks.Keys)
        {
            if (decimal.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                value > max)
            {
                max = value;
            }
        }
        return max;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString(CultureInfo.InvariantCulture);
}
