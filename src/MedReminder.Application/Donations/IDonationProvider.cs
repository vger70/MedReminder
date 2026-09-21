namespace MedReminder.Application.Donations;

// Provider port (A6, docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.1).
//
// A provider maps a requested donation to a PUBLIC payment URL; it
// does NOT create a server-side charge. v1 is a pure Payment-Link
// lookup with no I/O and no backend call, so the port is synchronous
// (decision §17 item 1). The async CreateDonationAsync signature is
// reserved for the v2 backend (§14) where an await actually means
// something.
public interface IDonationProvider
{
    // Which provider this adapter serves.
    DonationProvider Kind { get; }

    // Display name shown in the UI (e.g. "Stripe", "PayPal").
    string Name { get; }

    // True when a non-empty, valid "custom" ("choose your amount")
    // link is configured. Derived from the config — there is no
    // separate flag to keep in sync.
    bool SupportsCustomAmount { get; }

    // Resolves a fixed tier to its own pre-made fixed-amount link. The
    // amount is never injected into the URL.
    DonationLaunchResult Resolve(decimal amount);

    // Resolves the provider-native "choose your amount" link, to be
    // opened verbatim. The app never appends a number to it.
    DonationLaunchResult ResolveCustom();
}
