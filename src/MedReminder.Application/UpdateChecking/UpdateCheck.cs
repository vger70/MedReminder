using System.Text.Json;

namespace MedReminder.Application.UpdateChecking;

// Passive update-check port. The Infrastructure layer implements it
// by querying the GitHub Releases API. The UI calls it on startup
// (opt-in via UserSettings.CheckForUpdatesOnStartup) and from the
// Help menu on demand.
//
// The check never downloads or installs anything: it only surfaces
// the latest release tag so the user can visit the release page and
// upgrade manually.
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken);
}

public enum UpdateCheckStatus
{
    // The current build is at or ahead of the latest published
    // release tag.
    UpToDate,

    // A published release with a higher version tag exists.
    NewVersionAvailable,

    // The remote could not be reached, the response was malformed,
    // or the tag could not be parsed as a version. Callers must
    // treat this as "unknown" — never as "up to date".
    Error,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion,
    string? LatestTag,
    string? ReleaseUrl,
    string? ErrorMessage)
{
    public static UpdateCheckResult UpToDate() =>
        new(UpdateCheckStatus.UpToDate, null, null, null, null);

    public static UpdateCheckResult NewVersion(Version latest, string tag, string url) =>
        new(UpdateCheckStatus.NewVersionAvailable, latest, tag, url, null);

    public static UpdateCheckResult Error(string message) =>
        new(UpdateCheckStatus.Error, null, null, null, message);
}

// Pure JSON parser for the GitHub Releases API payload. Kept in the
// Application layer so it can be unit-tested without any HTTP
// dependency; the Infrastructure adapter just wires the HttpClient
// to it.
public static class GitHubReleaseParser
{
    public static UpdateCheckResult Parse(string json, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(currentVersion);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return UpdateCheckResult.Error("Malformed release payload: " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;

            // Skip drafts and prereleases — the passive check only
            // surfaces stable public releases.
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                return UpdateCheckResult.UpToDate();
            }
            if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
            {
                return UpdateCheckResult.UpToDate();
            }

            if (!root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return UpdateCheckResult.Error("Release payload missing tag_name.");
            }

            var tag = tagElement.GetString();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return UpdateCheckResult.Error("Empty release tag.");
            }

            if (!TryParseTag(tag!, out var remote))
            {
                return UpdateCheckResult.Error("Unrecognised release tag: " + tag);
            }

            var releaseUrl = root.TryGetProperty("html_url", out var urlElement) &&
                             urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            var normalizedCurrent = Normalize(currentVersion);
            var normalizedRemote = Normalize(remote);

            return normalizedRemote.CompareTo(normalizedCurrent) > 0
                ? UpdateCheckResult.NewVersion(remote, tag!, releaseUrl)
                : UpdateCheckResult.UpToDate();
        }
    }

    // Accepts tags of the form "v2.0.1", "V2.0.1", "2.0.1",
    // "v2.0.1-rc1" (the suffix is stripped and ignored). Returns
    // false for anything Version.TryParse cannot consume.
    public static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var trimmed = tag.Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return Version.TryParse(trimmed, out version!);
    }

    // Compare on major.minor.build only. The revision component is
    // never bumped by the release workflow (it stays at 0), so
    // including it in the comparison would produce false positives
    // when the SDK inserts a build number.
    private static Version Normalize(Version v)
    {
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
