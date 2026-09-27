using System.Text.Json.Serialization;

namespace MedReminder.Application.Abstractions;

// Cloud storage accounts (B.1 Phase 4a, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8; C.3++ Phase 2): the providers whose
// API the app talks to directly, as opposed to a folder kept in sync by
// the provider's own client. Sign-in happens on the device; the token
// cache stays on it, protected by the platform (DPAPI on Windows).
[JsonConverter(typeof(JsonStringEnumConverter<CloudProvider>))]
public enum CloudProvider
{
    OneDrive = 1,
}

// Id: the provider's stable account identifier (MSAL home account id).
// UserName: shown in the UI only, never logged.
public sealed record CloudAccount(CloudProvider Provider, string Id, string UserName);

public interface ICloudAccountService
{
    // False when the build has no app registration for the provider.
    bool IsAvailable(CloudProvider provider);

    // Interactive sign-in in the system browser. With accountId, signs in
    // again to that account (expired or revoked session).
    Task<CloudAccount> SignInAsync(CloudProvider provider, string? accountId, CancellationToken cancellationToken);

    // The account if it is still signed in on this device.
    Task<CloudAccount?> FindAsync(CloudProvider provider, string accountId, CancellationToken cancellationToken);
}

// The provider session ended (password change, revoked consent, long
// inactivity): an interactive sign-in is needed before the next request.
public sealed class CloudSignInRequiredException : Exception
{
    public CloudSignInRequiredException(CloudProvider provider, Exception? inner = null)
        : base($"Sign in to {provider} again to continue.", inner)
    {
        Provider = provider;
    }

    public CloudProvider Provider { get; }
}
