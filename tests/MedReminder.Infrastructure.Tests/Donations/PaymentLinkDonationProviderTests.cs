using FluentAssertions;
using MedReminder.Application.Donations;
using MedReminder.Infrastructure.Donations;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Donations;

// Verifies the Payment-Link provider adapters map amounts to the
// configured URLs, return the right failure when a key is absent, and
// expose the custom link verbatim (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §12.2).
public class PaymentLinkDonationProviderTests
{
    private const string Two = "https://donate.stripe.com/two";
    private const string Ten = "https://donate.stripe.com/ten";
    private const string Custom = "https://donate.stripe.com/choose";

    private static StripeDonationProvider Stripe(Dictionary<string, string> links) =>
        new(new ProviderOptions { Enabled = true, PaymentLinks = links });

    [Fact]
    public void Resolve_maps_amount_to_configured_url()
    {
        var provider = Stripe(new() { ["2"] = Two, ["10"] = Ten });

        var result = provider.Resolve(10m);

        result.Success.Should().BeTrue();
        result.ResolvedUrl.Should().Be(new Uri(Ten));
    }

    [Fact]
    public void Resolve_reports_missing_url_when_key_absent()
    {
        var provider = Stripe(new() { ["2"] = Two });

        var result = provider.Resolve(10m);

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(DonationFailureReason.MissingUrl);
    }

    [Fact]
    public void Resolve_reports_not_https_for_insecure_link()
    {
        var provider = Stripe(new() { ["10"] = "http://donate.stripe.com/ten" });

        var result = provider.Resolve(10m);

        result.Reason.Should().Be(DonationFailureReason.NotHttps);
    }

    [Fact]
    public void Resolve_reports_malformed_for_non_uri_link()
    {
        var provider = Stripe(new() { ["10"] = "not a url" });

        var result = provider.Resolve(10m);

        result.Reason.Should().Be(DonationFailureReason.MalformedUrl);
    }

    [Fact]
    public void ResolveCustom_returns_the_custom_link_verbatim()
    {
        var provider = Stripe(new() { ["2"] = Two, ["custom"] = Custom });

        provider.SupportsCustomAmount.Should().BeTrue();

        var result = provider.ResolveCustom();

        result.Success.Should().BeTrue();
        result.ResolvedUrl!.ToString().Should().Be(Custom);
        result.ResolvedUrl.Query.Should().BeEmpty();
    }

    [Fact]
    public void SupportsCustomAmount_is_false_when_custom_key_absent()
    {
        var provider = Stripe(new() { ["2"] = Two });

        provider.SupportsCustomAmount.Should().BeFalse();
        provider.ResolveCustom().Reason.Should().Be(DonationFailureReason.CustomAmountUnavailable);
    }

    [Fact]
    public void SupportsCustomAmount_is_false_when_custom_key_blank()
    {
        var provider = Stripe(new() { ["custom"] = "   " });

        provider.SupportsCustomAmount.Should().BeFalse();
    }

    [Fact]
    public void Kind_and_name_reflect_the_provider()
    {
        var stripe = Stripe(new());
        stripe.Kind.Should().Be(DonationProvider.Stripe);
        stripe.Name.Should().Be("Stripe");

        var paypal = new PayPalDonationProvider(new ProviderOptions());
        paypal.Kind.Should().Be(DonationProvider.PayPal);
        paypal.Name.Should().Be("PayPal");
    }
}
