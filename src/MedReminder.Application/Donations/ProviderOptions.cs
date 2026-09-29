namespace MedReminder.Application.Donations;

// Per-provider configuration (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §4.1). A single type serves
// both Stripe and PayPal — they differ only by which links they hold,
// not by shape. This is what makes adding Ko-fi / Buy Me a Coffee
// later a config-only + one-adapter change.
public sealed class ProviderOptions
{
    public bool Enabled { get; set; }

    // Payment-Link map.
    //   Fixed tiers: "2" / "5" / "10" / "20" -> a pre-made
    //     fixed-amount HTTPS Payment Link.
    //   Custom: the reserved key "custom" -> a provider-native
    //     "choose your amount" HTTPS link.
    // The "custom" value is opened VERBATIM; the app never appends an
    // amount to it, nor to any tier link.
    public Dictionary<string, string> PaymentLinks { get; set; } = new();
}
