using FluentAssertions;
using MedReminder.Application.Donations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Donations;

// Covers the DonationService validation pipeline (§6) and the launch
// behavior (§7) with a fake provider and a fake launcher — no real
// payments and no real browser (docs/ANALYSIS-A6-DONATION-SUPPORT.md
// §12.1). Both the fixed tiers and the provider-native custom link are
// exercised, including the guarantee that no amount is ever appended
// to any URL.
public class DonationServiceTests
{
    private const string Stripe2 = "https://donate.stripe.com/test_2eur";
    private const string Stripe5 = "https://donate.stripe.com/test_5eur";
    private const string Stripe10 = "https://donate.stripe.com/test_10eur";
    private const string Stripe20 = "https://donate.stripe.com/test_20eur";
    private const string StripeCustom = "https://donate.stripe.com/test_choose_amount";

    private static Dictionary<string, string> FullTierMap() => new()
    {
        ["2"] = Stripe2,
        ["5"] = Stripe5,
        ["10"] = Stripe10,
        ["20"] = Stripe20,
    };

    private static DonationOptions Options(
        bool enabled = true,
        bool stripeEnabled = true,
        Dictionary<string, string>? stripeLinks = null,
        bool payPalEnabled = false,
        Dictionary<string, string>? payPalLinks = null)
    {
        return new DonationOptions
        {
            Enabled = enabled,
            Currency = "EUR",
            Stripe = new ProviderOptions
            {
                Enabled = stripeEnabled,
                PaymentLinks = stripeLinks ?? FullTierMap(),
            },
            PayPal = new ProviderOptions
            {
                Enabled = payPalEnabled,
                PaymentLinks = payPalLinks ?? new Dictionary<string, string>(),
            },
        };
    }

    private static (DonationService service, FakeUrlLauncher launcher) Build(
        DonationOptions options, FakeUrlLauncher? launcher = null)
    {
        launcher ??= new FakeUrlLauncher(succeeds: true);
        var providers = new IDonationProvider[]
        {
            new FakeDonationProvider(DonationProvider.Stripe, "Stripe", options.Stripe.PaymentLinks),
            new FakeDonationProvider(DonationProvider.PayPal, "PayPal", options.PayPal.PaymentLinks),
        };
        var service = new DonationService(
            options, providers, launcher, NullLogger<DonationService>.Instance);
        return (service, launcher);
    }

    // ---------------- Fixed tiers — happy path ----------------

    [Theory]
    [InlineData(2, Stripe2)]
    [InlineData(5, Stripe5)]
    [InlineData(10, Stripe10)]
    [InlineData(20, Stripe20)]
    public void Valid_tier_resolves_correct_url_and_launches_once(int amount, string expectedUrl)
    {
        var (service, launcher) = Build(Options());

        var result = service.Donate(DonationProvider.Stripe, amount);

        result.Success.Should().BeTrue();
        result.UserMessageKey.Should().Be(DonationMessageKeys.Launched);
        result.ResolvedUrl.Should().Be(new Uri(expectedUrl));
        launcher.LaunchCount.Should().Be(1);
        launcher.Launched[0].Should().Be(new Uri(expectedUrl));
    }

    // ---------------- Amount validation ----------------

    [Fact]
    public void Zero_amount_is_invalid_and_does_not_launch()
    {
        var (service, launcher) = Build(Options());

        var result = service.Donate(DonationProvider.Stripe, 0m);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(DonationFailureReason.InvalidAmount);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Negative_amount_is_invalid_and_does_not_launch()
    {
        var (service, launcher) = Build(Options());

        var result = service.Donate(DonationProvider.Stripe, -5m);

        result.Reason.Should().Be(DonationFailureReason.InvalidAmount);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Amount_above_max_tier_is_unsupported_and_does_not_launch()
    {
        var (service, launcher) = Build(Options());

        var result = service.Donate(DonationProvider.Stripe, 50m);

        result.Reason.Should().Be(DonationFailureReason.UnsupportedAmount);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Unsupported_in_range_amount_without_link_reports_missing_url()
    {
        // €7 is below the max tier (20) but has no configured link.
        var (service, launcher) = Build(Options());

        var result = service.Donate(DonationProvider.Stripe, 7m);

        result.Reason.Should().Be(DonationFailureReason.MissingUrl);
        launcher.LaunchCount.Should().Be(0);
    }

    // ---------------- Feature / provider gates ----------------

    [Fact]
    public void Feature_disabled_reports_disabled_and_does_not_launch()
    {
        var (service, launcher) = Build(Options(enabled: false));

        var result = service.Donate(DonationProvider.Stripe, 10m);

        result.Reason.Should().Be(DonationFailureReason.Disabled);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Provider_disabled_reports_provider_disabled_and_does_not_launch()
    {
        var (service, launcher) = Build(Options(stripeEnabled: false));

        var result = service.Donate(DonationProvider.Stripe, 10m);

        result.Reason.Should().Be(DonationFailureReason.ProviderDisabled);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Enabled_provider_with_empty_link_map_reports_incomplete_config()
    {
        var (service, launcher) = Build(Options(stripeLinks: new Dictionary<string, string>()));

        var result = service.Donate(DonationProvider.Stripe, 10m);

        result.Reason.Should().Be(DonationFailureReason.IncompleteConfig);
        launcher.LaunchCount.Should().Be(0);
    }

    // ---------------- URL shape validation ----------------

    [Fact]
    public void Malformed_url_is_rejected_and_does_not_launch()
    {
        var links = FullTierMap();
        links["10"] = "not a url";
        var (service, launcher) = Build(Options(stripeLinks: links));

        var result = service.Donate(DonationProvider.Stripe, 10m);

        result.Reason.Should().Be(DonationFailureReason.MalformedUrl);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Non_https_url_is_rejected_and_does_not_launch()
    {
        var links = FullTierMap();
        links["10"] = "http://donate.stripe.com/insecure";
        var (service, launcher) = Build(Options(stripeLinks: links));

        var result = service.Donate(DonationProvider.Stripe, 10m);

        result.Reason.Should().Be(DonationFailureReason.NotHttps);
        launcher.LaunchCount.Should().Be(0);
    }

    // ---------------- Launch outcomes ----------------

    [Fact]
    public void Successful_launch_returns_success_with_launched_key()
    {
        var launcher = new FakeUrlLauncher(succeeds: true);
        var (service, _) = Build(Options(), launcher);

        var result = service.Donate(DonationProvider.Stripe, 5m);

        result.Success.Should().BeTrue();
        result.UserMessageKey.Should().Be("Ui.Donate.Launched");
        launcher.LaunchCount.Should().Be(1);
    }

    [Fact]
    public void Launch_failure_maps_to_launch_failed_with_non_technical_key()
    {
        var launcher = new FakeUrlLauncher(succeeds: false, error: "browser missing");
        var (service, _) = Build(Options(), launcher);

        var result = service.Donate(DonationProvider.Stripe, 5m);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(DonationFailureReason.LaunchFailed);
        result.UserMessageKey.Should().Be("Ui.Donate.Error.LaunchFailed");
        // The launch was attempted exactly once (all validation passed).
        launcher.LaunchCount.Should().Be(1);
    }

    // ---------------- Custom amount ----------------

    [Fact]
    public void Custom_amount_opens_configured_link_verbatim()
    {
        var links = FullTierMap();
        links["custom"] = StripeCustom;
        var (service, launcher) = Build(Options(stripeLinks: links));

        service.SupportsCustomAmount(DonationProvider.Stripe).Should().BeTrue();

        var result = service.DonateCustom(DonationProvider.Stripe);

        result.Success.Should().BeTrue();
        launcher.LaunchCount.Should().Be(1);
        // The launched Uri equals the config value, unchanged — no
        // query amount appended.
        launcher.Launched[0].Should().Be(new Uri(StripeCustom));
        launcher.Launched[0].ToString().Should().Be(StripeCustom);
        launcher.Launched[0].Query.Should().BeEmpty();
    }

    [Fact]
    public void Custom_amount_without_link_reports_unavailable_and_does_not_launch()
    {
        // No "custom" key configured.
        var (service, launcher) = Build(Options());

        service.SupportsCustomAmount(DonationProvider.Stripe).Should().BeFalse();

        var result = service.DonateCustom(DonationProvider.Stripe);

        result.Reason.Should().Be(DonationFailureReason.CustomAmountUnavailable);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Custom_amount_with_non_https_link_is_rejected_and_does_not_launch()
    {
        var links = FullTierMap();
        links["custom"] = "http://donate.stripe.com/choose";
        var (service, launcher) = Build(Options(stripeLinks: links));

        // The key is present, so the custom option is offered — but the
        // link fails the HTTPS check at resolve time and never launches.
        service.SupportsCustomAmount(DonationProvider.Stripe).Should().BeTrue();

        var result = service.DonateCustom(DonationProvider.Stripe);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(DonationFailureReason.NotHttps);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public void Custom_amount_with_malformed_link_is_rejected_and_does_not_launch()
    {
        var links = FullTierMap();
        links["custom"] = "not a url";
        var (service, launcher) = Build(Options(stripeLinks: links));

        service.SupportsCustomAmount(DonationProvider.Stripe).Should().BeTrue();

        var result = service.DonateCustom(DonationProvider.Stripe);

        result.Reason.Should().Be(DonationFailureReason.MalformedUrl);
        launcher.LaunchCount.Should().Be(0);
    }

    // ---------------- Availability helpers ----------------

    [Fact]
    public void Feature_available_reflects_enabled_state_and_provider_config()
    {
        Build(Options()).service.IsFeatureAvailable.Should().BeTrue();
        Build(Options(enabled: false)).service.IsFeatureAvailable.Should().BeFalse();
        Build(Options(stripeEnabled: false)).service.IsFeatureAvailable.Should().BeFalse();
        Build(Options(stripeLinks: new Dictionary<string, string>())).service.IsFeatureAvailable.Should().BeFalse();
    }
}
