using MedReminder.Application.Donations;

namespace MedReminder.Infrastructure.Donations;

// PayPal adapter (A6, docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.2). Pure
// lookup over the bound PayPal ProviderOptions — no PayPal SDK, no
// HttpClient, no client secret, no access token. The fixed tiers are
// hosted donate-button URLs; the "custom" key is a PayPal donate
// button configured without a fixed amount, so the payer chooses the
// amount on PayPal's page.
public sealed class PayPalDonationProvider : PaymentLinkDonationProvider
{
    public PayPalDonationProvider(ProviderOptions options)
        : base(options)
    {
    }

    public override DonationProvider Kind => DonationProvider.PayPal;

    public override string Name => "PayPal";
}
