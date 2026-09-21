namespace MedReminder.Application.Donations;

// Configuration model for the donation feature (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §4.1). Bound from the
// "Donations" section of IConfiguration, sourced from the shared,
// admin-managed donations.settings.json at the %LOCALAPPDATA%\
// MedReminder\ root (§4.2).
//
// The file holds only PUBLIC payment URLs, never secrets, so — unlike
// smtp.protected — it is plain JSON with no DPAPI encryption (§9).
// A missing file or missing section binds to Enabled = false, which
// silently turns the whole feature off (§4.2).
public sealed class DonationOptions
{
    public const string SectionName = "Donations";

    public bool Enabled { get; set; }

    public string Currency { get; set; } = "EUR";

    public ProviderOptions Stripe { get; set; } = new();

    public ProviderOptions PayPal { get; set; } = new();
}
