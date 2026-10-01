using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using MedReminder.Application.Catalogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Catalogue;

// IShortageFeedClient over the raw files the shortage workflow commits
// to data/it/shortages/ (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3). Same
// rules as GitHubRawCatalogueFeedClient: HTTPS only (plain HTTP for a
// loopback test server), no redirects, the manifest capped at 4 KB and
// the list at ShortagesMaxDownloadBytes. Singleton: the HttpClient is
// reused across calls.
public sealed class GitHubRawShortageFeedClient : IShortageFeedClient, IDisposable
{
    private const int MaxManifestBytes = 4 * 1024;

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<CatalogueFeedOptions> _options;
    private readonly ILogger<GitHubRawShortageFeedClient> _log;
    private readonly bool _ownsClient;

    public GitHubRawShortageFeedClient(
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawShortageFeedClient> log)
        : this(
            new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
            options, log, ownsClient: true)
    {
    }

    // Test constructor: a fake handler replaces the network.
    internal GitHubRawShortageFeedClient(
        HttpMessageHandler handler,
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawShortageFeedClient> log,
        bool ownsClient = false)
    {
        _http = new HttpClient(handler, disposeHandler: ownsClient) { Timeout = Timeout.InfiniteTimeSpan };
        var version = (Assembly.GetEntryAssembly() ?? typeof(GitHubRawShortageFeedClient).Assembly)
            .GetName().Version?.ToString(3) ?? "dev";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MedReminder", version));
        _options = options;
        _log = log;
        _ownsClient = ownsClient;
    }

    public async Task<ShortageFeedManifest?> GetManifestAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!TryBuildUri(options.ShortageManifestUrl(), out var uri))
        {
            _log.LogWarning("Shortage feed: the manifest URL is not an absolute HTTPS URL.");
            return null;
        }
        try
        {
            var bytes = await GetCappedAsync(uri, MaxManifestBytes, options.ManifestTimeoutSeconds, cancellationToken);
            if (bytes is null) return null;
            if (!ShortageFeedParser.TryParseManifest(Encoding.UTF8.GetString(bytes), out var manifest, out var error))
            {
                _log.LogWarning("Shortage feed: {Error}", error);
                return null;
            }
            return manifest;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
        {
            _log.LogInformation("Shortage feed: the manifest could not be read: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<byte[]> DownloadAsync(ShortageFeedManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var options = _options.CurrentValue;
        if (manifest.Size > options.ShortagesMaxDownloadBytes)
        {
            throw new InvalidDataException($"The list is {manifest.Size} bytes, above the {options.ShortagesMaxDownloadBytes}-byte limit.");
        }
        if (!TryBuildUri(options.ShortageFileUrl(manifest.File), out var uri))
        {
            throw new InvalidOperationException("The shortage list URL is not an absolute HTTPS URL.");
        }
        return await GetCappedAsync(uri, (int)Math.Min(int.MaxValue, options.ShortagesMaxDownloadBytes),
                options.DownloadTimeoutSeconds, cancellationToken)
            ?? throw new InvalidDataException("The shortage list could not be downloaded within its size limit.");
    }

    // Null on an HTTP error status or a body above `limit`.
    private async Task<byte[]?> GetCappedAsync(Uri uri, int limit, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogInformation("Shortage feed: request answered HTTP {Status}.", (int)response.StatusCode);
            return null;
        }
        if (response.Content.Headers.ContentLength > limit) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool TryBuildUri(string? value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
        {
            return false;
        }
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
