using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
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
// (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.2): only an
// absolute https URL whose host is in the current list, as published,
// with nothing added. Anything else is refused and logged without the
// URL. A successful open logs the region code only, never a
// prescription code.
public sealed class RegionalServiceLinkLauncher
{
    private readonly IRegionalServicesListStore _list;
    private readonly IUrlOpener _opener;
    private readonly ILogger<RegionalServiceLinkLauncher> _log;

    public RegionalServiceLinkLauncher(IRegionalServicesListStore list, IUrlOpener opener,
        ILogger<RegionalServiceLinkLauncher> log)
    {
        _list = list;
        _opener = opener;
        _log = log;
    }

    // False when the link was refused or the browser could not be started.
    public bool Open(string regionCode, string? url)
    {
        if (!IsAllowed(url, out var uri))
        {
            _log.LogWarning("Regional service link refused for region {Region}: not an https URL of the list.",
                ItalianRegions.IsValid(regionCode) ? regionCode : "?");
            return false;
        }
        try
        {
            _opener.Open(uri);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Regional service could not be opened for region {Region}: {Error}",
                regionCode, ex.GetType().Name);
            return false;
        }
        _log.LogInformation("Regional service opened: {Region}", regionCode);
        return true;
    }

    // An https URL of a host the current list links to.
    public bool IsAllowed(string? url, out Uri uri)
    {
        uri = null!;
        if (RegionalServicesFeedParser.ParseHttps(url) is not { } parsed) return false;
        if (_list.Load() is not { } list || !list.Hosts.Contains(parsed.IdnHost)) return false;
        uri = parsed;
        return true;
    }
}

// Hands a URL to the default browser (the shell on Windows). Implemented
// by the UI.
public interface IUrlOpener
{
    void Open(Uri uri);
}
