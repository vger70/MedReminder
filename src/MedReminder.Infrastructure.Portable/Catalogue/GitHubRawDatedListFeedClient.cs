using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Catalogue;

// IDatedListFeedClient over the raw files a feed workflow publishes to
// `{BaseUrl}it/<folder>/` (the shortage list, docs/notes/EVOLUTION-PROPOSALS-2.md
// §3.3; the transparency list, ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK §2.4).
// Same rules as GitHubRawCatalogueFeedClient: HTTPS only (plain HTTP for a
// loopback test server), no redirects, the manifest capped at 4 KB and
// the list at the definition's download limit. Singleton: the HttpClient
// is reused across calls.
public abstract class GitHubRawDatedListFeedClient<TList> : IDatedListFeedClient<TList>, IDisposable
    where TList : class
{
    private const int MaxManifestBytes = 4 * 1024;

    private readonly DatedListFeedDefinition<TList> _definition;
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<CatalogueFeedOptions> _options;
    private readonly ILogger _log;
    private readonly bool _ownsClient;

    protected GitHubRawDatedListFeedClient(
        DatedListFeedDefinition<TList> definition,
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger log)
        : this(
            definition,
            new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
            options, log, ownsClient: true)
    {
    }

    // Test constructor: a fake handler replaces the network.
    protected GitHubRawDatedListFeedClient(
        DatedListFeedDefinition<TList> definition,
        HttpMessageHandler handler,
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger log,
        bool ownsClient)
    {
        _definition = definition;
        _http = new HttpClient(handler, disposeHandler: ownsClient) { Timeout = Timeout.InfiniteTimeSpan };
        var version = (Assembly.GetEntryAssembly() ?? typeof(GitHubRawDatedListFeedClient<TList>).Assembly)
            .GetName().Version?.ToString(3) ?? "dev";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MedReminder", version));
        _options = options;
        _log = log;
        _ownsClient = ownsClient;
    }

    public async Task<DatedFeedManifest?> GetManifestAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var name = _definition.Name;
        if (!TryBuildUri(options.ItalianFeedFolder(_definition.Folder) + "latest.json", out var uri))
        {
            _log.LogWarning("{Feed} feed: the manifest URL is not an absolute HTTPS URL.", name);
            return null;
        }
        try
        {
            var bytes = await GetCappedAsync(uri, MaxManifestBytes, options.ManifestTimeoutSeconds, cancellationToken);
            if (bytes is null) return null;
            if (!_definition.TryParseManifest(Encoding.UTF8.GetString(bytes), out var manifest, out var error))
            {
                _log.LogWarning("{Feed} feed: {Error}", name, error);
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
            _log.LogInformation("{Feed} feed: the manifest could not be read: {Message}", name, ex.Message);
            return null;
        }
    }

    // `manifest.File` was validated by the manifest parser
    // (`<prefix><yyyymmdd>.json`), so it cannot inject a path or a query.
    public async Task<byte[]> DownloadAsync(DatedFeedManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var options = _options.CurrentValue;
        var limit = _definition.MaxDownloadBytes(options);
        if (manifest.Size > limit)
        {
            throw new InvalidDataException($"The list is {manifest.Size} bytes, above the {limit}-byte limit.");
        }
        if (!TryBuildUri(options.ItalianFeedFolder(_definition.Folder) + manifest.File, out var uri))
        {
            throw new InvalidOperationException($"The {_definition.Name} list URL is not an absolute HTTPS URL.");
        }
        return await GetCappedAsync(uri, (int)Math.Min(int.MaxValue, limit), options.DownloadTimeoutSeconds, cancellationToken)
            ?? throw new InvalidDataException($"The {_definition.Name} list could not be downloaded within its size limit.");
    }

    // Null on an HTTP error status or a body above `limit`.
    private async Task<byte[]?> GetCappedAsync(Uri uri, int limit, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogInformation("{Feed} feed: request answered HTTP {Status}.", _definition.Name, (int)response.StatusCode);
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
        GC.SuppressFinalize(this);
    }
}

public sealed class GitHubRawShortageFeedClient : GitHubRawDatedListFeedClient<ShortageList>, IShortageFeedClient
{
    public GitHubRawShortageFeedClient(IOptionsMonitor<CatalogueFeedOptions> options, ILogger<GitHubRawShortageFeedClient> log)
        : base(ShortageFeedDefinition.Instance, options, log)
    {
    }

    internal GitHubRawShortageFeedClient(HttpMessageHandler handler, IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawShortageFeedClient> log, bool ownsClient = false)
        : base(ShortageFeedDefinition.Instance, handler, options, log, ownsClient)
    {
    }
}

public sealed class GitHubRawEquivalenceFeedClient : GitHubRawDatedListFeedClient<EquivalenceList>, IEquivalenceFeedClient
{
    public GitHubRawEquivalenceFeedClient(IOptionsMonitor<CatalogueFeedOptions> options, ILogger<GitHubRawEquivalenceFeedClient> log)
        : base(EquivalenceFeedDefinition.Instance, options, log)
    {
    }

    internal GitHubRawEquivalenceFeedClient(HttpMessageHandler handler, IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawEquivalenceFeedClient> log, bool ownsClient = false)
        : base(EquivalenceFeedDefinition.Instance, handler, options, log, ownsClient)
    {
    }
}
