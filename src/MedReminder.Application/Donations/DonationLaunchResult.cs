namespace MedReminder.Application.Donations;

// Result of a donation resolve/launch attempt (A6,
// docs/ANALYSIS-A6-DONATION-SUPPORT.md §3.1).
//
// The result carries a localization KEY, not rendered text, so the UI
// stays the single place that resolves user-visible strings. The
// resolved URL is kept for logging/launch only and is never shown raw
// to the user (§9, §10).
public sealed record DonationLaunchResult(
    bool Success,
    DonationFailureReason? Reason,
    Uri? ResolvedUrl,
    string? UserMessageKey)
{
    // Convenience factory for the single success shape.
    public static DonationLaunchResult Ok(Uri resolvedUrl, string userMessageKey) =>
        new(Success: true, Reason: null, ResolvedUrl: resolvedUrl, UserMessageKey: userMessageKey);

    // Convenience factory for a failure carrying a specific reason and
    // its localization key.
    public static DonationLaunchResult Fail(DonationFailureReason reason, string userMessageKey) =>
        new(Success: false, Reason: reason, ResolvedUrl: null, UserMessageKey: userMessageKey);
}
