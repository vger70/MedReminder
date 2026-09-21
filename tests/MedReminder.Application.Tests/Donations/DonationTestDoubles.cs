using MedReminder.Application.Donations;

namespace MedReminder.Application.Tests.Donations;

// Records every launch attempt so tests can assert on the exact Uri
// and how many times the browser hand-off was invoked. Never spawns a
// real process.
internal sealed class FakeUrlLauncher : IUrlLauncher
{
    private readonly bool _succeeds;
    private readonly string? _error;

    public FakeUrlLauncher(bool succeeds = true, string? error = null)
    {
        _succeeds = succeeds;
        _error = error;
    }

    public List<Uri> Launched { get; } = new();

    public int LaunchCount => Launched.Count;

    public bool TryLaunch(Uri httpsUrl, out string? error)
    {
        Launched.Add(httpsUrl);
        if (_succeeds)
        {
            error = null;
            return true;
        }
        error = _error ?? "fake launch failure";
        return false;
    }
}

// Configurable provider double. Resolves fixed tiers and the custom
// link from an in-memory map, applying the same URL parse / HTTPS
// rules the real adapters apply — so the DonationService pipeline can
// be exercised end to end without Infrastructure.
internal sealed class FakeDonationProvider : IDonationProvider
{
    private readonly Dictionary<string, string> _links;

    public FakeDonationProvider(
        DonationProvider kind,
        string name,
        Dictionary<string, string> links)
    {
        Kind = kind;
        Name = name;
        _links = links;
    }

    public DonationProvider Kind { get; }

    public string Name { get; }

    // Presence-based, mirroring the real adapters: the "custom" key
    // exists and is non-empty. URL shape (HTTPS / malformed) is
    // validated by ResolveCustom, not here.
    public bool SupportsCustomAmount =>
        _links.TryGetValue("custom", out var url) && !string.IsNullOrWhiteSpace(url);

    public DonationLaunchResult Resolve(decimal amount)
    {
        var key = ((int)amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_links.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.MissingUrl);
        }
        return ValidateAndWrap(raw);
    }

    public DonationLaunchResult ResolveCustom()
    {
        if (!_links.TryGetValue("custom", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.CustomAmountUnavailable);
        }
        return ValidateAndWrap(raw);
    }

    private static DonationLaunchResult ValidateAndWrap(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return DonationMessageKeys.Failure(DonationFailureReason.MalformedUrl);
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return DonationMessageKeys.Failure(DonationFailureReason.NotHttps);
        }
        return DonationLaunchResult.Ok(uri, DonationMessageKeys.Launched);
    }
}
