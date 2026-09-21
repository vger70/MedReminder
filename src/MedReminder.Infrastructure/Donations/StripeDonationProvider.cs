using MedReminder.Application.Donations;

namespace MedReminder.Infrastructure.Donations;

// Stripe adapter (A6, docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.2). Pure
// lookup over the bound Stripe ProviderOptions — no Stripe SDK, no
// HttpClient, no secret key. The fixed tiers are pre-made Stripe
// Payment Links; the "custom" key is a Stripe Payment Link configured
// with "customer chooses price".
public sealed class StripeDonationProvider : PaymentLinkDonationProvider
{
    public StripeDonationProvider(ProviderOptions options)
        : base(options)
    {
    }

    public override DonationProvider Kind => DonationProvider.Stripe;

    public override string Name => "Stripe";
}
