using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using MedReminder.Application.UpdateChecking;
using Microsoft.Extensions.Logging;

namespace MedReminder.Infrastructure.UpdateChecking;

// IUpdateChecker adapter over the public GitHub Releases API.
//
// Endpoint: https://api.github.com/repos/vger70/MedReminder/releases/latest
//
// The check is best-effort:
//   * Timeouts, DNS failures, 4xx/5xx and rate-limit responses all
//     collapse into UpdateCheckStatus.Error with a short message.
//   * Drafts and prereleases are treated as "up to date" (see
//     GitHubReleaseParser).
//
// Nothing is downloaded or executed here — the caller receives the
// latest release URL and shows it to the user for a manual upgrade.
public sealed class GitHubUpdateChecker : IUpdateChecker, IDisposable
{
    private const string DefaultLatestReleaseUrl =
        "https://api.github.com/repos/vger70/MedReminder/releases/latest";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http;
    private readonly ILogger<GitHubUpdateChecker> _log;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> log)
        : this(BuildDefaultClient(), new Uri(DefaultLatestReleaseUrl), log, ownsClient: true)
    {
    }

    // Test-friendly constructor: lets Infrastructure tests inject a
    // fake HttpMessageHandler and a custom endpoint.
    internal GitHubUpdateChecker(
        HttpClient http,
        Uri endpoint,
        ILogger<GitHubUpdateChecker> log,
        bool ownsClient = false)
    {
        _http = http;
        _endpoint = endpoint;
        _log = log;
        _ownsClient = ownsClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.Error("No published release found.");
            }
            if ((int)response.StatusCode == 403)
            {
                return UpdateCheckResult.Error("Rate limit reached; try again later.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Error(
                    $"GitHub responded with HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = GitHubReleaseParser.Parse(body, currentVersion);

            _log.LogInformation(
                "Update check completed: {Status} (current={Current}, latest={Latest}).",
                result.Status, currentVersion, result.LatestVersion);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return UpdateCheckResult.Error("The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Error("Network error: " + ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Unexpected error during the update check.");
            return UpdateCheckResult.Error(ex.Message);
        }
    }

    private static HttpClient BuildDefaultClient()
    {
        var client = new HttpClient
        {
            Timeout = DefaultTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "MedReminder",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
