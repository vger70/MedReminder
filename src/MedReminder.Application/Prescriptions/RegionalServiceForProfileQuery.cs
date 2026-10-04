using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Donations;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;
using Microsoft.Extensions.Logging;

namespace MedReminder.Application.Prescriptions;

// What the "Regional prescription service" button offers
// (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.2, §3.3).
public enum RegionalServiceAvailability
{
    // The reference country is not Italy: the button is hidden.
    NotItaly,

    // The profile has no region yet: the button asks for it.
    NoRegion,

    // The list has no entry for the region: the button says so and
    // guesses no link.
    NoEntry,

    // Service holds the entry.
    Found,
}

public sealed record RegionalServiceForProfile(
    RegionalServiceAvailability Availability,
    string Region,
    RegionalHealthService? Service);

// The regional service of the current profile: none when the reference
// country (UserSettings.ReferenceCountry, an installation setting the
// caller reads) is not Italy, the profile has no region or the list has
// no entry for it.
public sealed class RegionalServiceForProfileQuery
{
    private readonly IProfileSettingsStore _profileSettings;
    private readonly IRegionalServicesListStore _list;

    public RegionalServiceForProfileQuery(IProfileSettingsStore profileSettings, IRegionalServicesListStore list)
    {
        _profileSettings = profileSettings;
        _list = list;
    }

    // Whether the button is shown at all. An invalid value falls back to
    // Italy, as the catalogue does.
    public static bool IsOffered(string? referenceCountry) => CatalogueFeedSelection.IsItaly(referenceCountry);

    public RegionalServiceForProfile Get(string? referenceCountry)
    {
        if (!IsOffered(referenceCountry)) return new(RegionalServiceAvailability.NotItaly, string.Empty, null);
        var region = _profileSettings.Read().GetValueOrDefault(ProfileSetting.Region)?.Trim() ?? string.Empty;
        if (!ItalianRegions.IsValid(region)) return new(RegionalServiceAvailability.NoRegion, string.Empty, null);
        return _list.Load()?.Find(region) is { } service
            ? new(RegionalServiceAvailability.Found, region, service)
            : new(RegionalServiceAvailability.NoEntry, region, null);
    }
}

// Opens a link of the regional services list
// (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.2): only a URL
// that is exactly one of the links of the region's entry in the current
// list (its web page or an app link), with nothing added. Anything else
// is refused. The log names neither the URL nor the region: which
// health service a person is registered with, and when they open it, is
// personal data (CLAUDE.md §5).
public sealed class RegionalServiceLinkLauncher
{
    private readonly IRegionalServicesListStore _list;
    private readonly IUrlLauncher _launcher;
    private readonly ILogger<RegionalServiceLinkLauncher> _log;

    public RegionalServiceLinkLauncher(IRegionalServicesListStore list, IUrlLauncher launcher,
        ILogger<RegionalServiceLinkLauncher> log)
    {
        _list = list;
        _launcher = launcher;
        _log = log;
    }

    // False when the link was refused or the browser could not be started.
    public bool Open(string regionCode, string? url)
    {
        if (!IsAllowed(regionCode, url, out var uri))
        {
            _log.LogWarning("Regional service link refused: not a link of the list.");
            return false;
        }
        if (!_launcher.TryLaunch(uri, out _))
        {
            // The shell's message may name the URL: not logged.
            _log.LogWarning("Regional service could not be opened in the browser.");
            return false;
        }
        _log.LogInformation("Regional service opened.");
        return true;
    }

    // One of the links of the region's entry, compared as absolute URIs.
    public bool IsAllowed(string regionCode, string? url, out Uri uri)
    {
        uri = null!;
        if (RegionalServicesFeedParser.ParseHttps(url) is not { } parsed) return false;
        if (_list.Load()?.Find(regionCode) is not { } service) return false;
        if (!service.Links().Any(link => string.Equals(link.AbsoluteUri, parsed.AbsoluteUri, StringComparison.Ordinal)))
            return false;
        uri = parsed;
        return true;
    }
}
