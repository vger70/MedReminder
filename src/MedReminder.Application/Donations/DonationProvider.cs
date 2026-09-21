namespace MedReminder.Application.Donations;

// Donation providers the app can send the user to (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §5.1).
//
// The enum lists future providers so the UI and service switch on a
// stable type; only Stripe and PayPal have adapters registered in v1.
// KoFi and BuyMeACoffee are reserved seams only — no adapter ships in
// v1 (§13). Adding one later is a config + one-adapter change, no
// UI/service rewrite.
public enum DonationProvider
{
    Stripe,
    PayPal,
    KoFi,          // reserved, not implemented in v1
    BuyMeACoffee,  // reserved, not implemented in v1
}
