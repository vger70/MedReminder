using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MedReminder.Application.Catalogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.Infrastructure.Catalogue;

// ICatalogueFeedClient over the raw files the download workflows commit
// to data/<country>/ (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md
// §4.2, §5.3; ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §5).
//
//   * HTTPS only (plain HTTP is accepted for a loopback host, for a
//     local test server). Redirects are not followed, so the response
//     always comes from the configured host.
//   * The manifest is capped at 4 KB; the archive at the feed's
//     MaxDownloadBytes, checked against Content-Length and again while
//     streaming.
//   * The archive is written to `<destination>.part`, hashed while
//     written, and renamed once complete.
//
// Per-call timeouts come from CatalogueFeedOptions. Singleton: the
// HttpClient is reused across calls.
public sealed class GitHubRawCatalogueFeedClient : ICatalogueFeedClient, IDisposable
{
    private const int MaxManifestBytes = 4 * 1024;
    private const int CopyBufferBytes = 81920;

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<CatalogueFeedOptions> _options;
    private readonly ILogger<GitHubRawCatalogueFeedClient> _log;
    private readonly bool _ownsClient;

    public GitHubRawCatalogueFeedClient(
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawCatalogueFeedClient> log)
        : this(
            new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            },
            options,
            log,
            ownsClient: true)
    {
    }

    // Test constructor: a fake handler replaces the network.
    internal GitHubRawCatalogueFeedClient(
        HttpMessageHandler handler,
        IOptionsMonitor<CatalogueFeedOptions> options,
        ILogger<GitHubRawCatalogueFeedClient> log,
        bool ownsClient = false)
    {
        _http = new HttpClient(handler, disposeHandler: ownsClient)
        {
            // Per-call timeouts are applied with linked tokens.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var version = (Assembly.GetEntryAssembly() ?? typeof(GitHubRawCatalogueFeedClient).Assembly)
            .GetName().Version?.ToString(3) ?? "dev";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MedReminder", version));
        _options = options;
        _log = log;
        _ownsClient = ownsClient;
    }

    public async Task<CatalogueFeedManifest?> GetLatestAsync(
        CatalogueFeedDescriptor feed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);

        var options = _options.CurrentValue;
        if (!TryBuildUri(options.ManifestUrlFor(feed), out var uri))
        {
            _log.LogWarning("Remote catalogue feed {Country}: the manifest URL is not an absolute HTTPS URL.", feed.Country.Value);
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.ManifestTimeoutSeconds)));
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogInformation(
                    "Remote catalogue feed {Country}: manifest request answered HTTP {Status}.",
                    feed.Country.Value, (int)response.StatusCode);
                return null;
            }
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
            {
                _log.LogWarning("Remote catalogue feed {Country}: the manifest exceeds {Limit} bytes.", feed.Country.Value, MaxManifestBytes);
                return null;
            }

            var bytes = await ReadCappedAsync(response.Content, MaxManifestBytes, timeout.Token);
            if (bytes is null)
            {
                _log.LogWarning("Remote catalogue feed {Country}: the manifest exceeds {Limit} bytes.", feed.Country.Value, MaxManifestBytes);
                return null;
            }

            if (!CatalogueFeedManifestParser.TryParse(Encoding.UTF8.GetString(bytes), feed, out var manifest, out var error))
            {
                _log.LogWarning("Remote catalogue feed {Country}: {Error}", feed.Country.Value, error);
                return null;
            }
            return manifest;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("Remote catalogue feed {Country}: the manifest request timed out.", feed.Country.Value);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _log.LogInformation(
                "Remote catalogue feed {Country}: network error reading the manifest: {Message}",
                feed.Country.Value, ex.Message);
            return null;
        }
        catch (IOException ex)
        {
            // HttpIOException and friends: the connection dropped while
            // the body was being read.
            _log.LogInformation(
                "Remote catalogue feed {Country}: the manifest body could not be read: {Message}",
                feed.Country.Value, ex.Message);
            return null;
        }
    }

    public async Task<CatalogueFeedDownload> DownloadAsync(
        CatalogueFeedDescriptor feed,
        CatalogueFeedManifest manifest,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var options = _options.CurrentValue;
        // Version is validated as six digits by the manifest parser, so
        // the substitution cannot inject a path or query.
        if (!TryBuildUri(options.SnapshotUrlFor(feed, manifest.Version), out var uri))
        {
            throw new InvalidOperationException("The snapshot URL is not an absolute HTTPS URL.");
        }

        var maxBytes = options.MaxDownloadBytesFor(feed);
        var part = destinationPath + ".part";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.DownloadTimeoutSeconds)));
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The snapshot request answered HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            }
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw new InvalidDataException(
                    $"The snapshot is {response.Content.Headers.ContentLength} bytes, above the {maxBytes}-byte limit.");
            }

            long length = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var target = new FileStream(
                part, FileMode.Create, FileAccess.Write, FileShare.None,
                CopyBufferBytes, FileOptions.Asynchronous))
            {
                var buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    length += read;
                    if (length > maxBytes)
                    {
                        throw new InvalidDataException($"The snapshot exceeds the {maxBytes}-byte limit.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }

            File.Move(part, destinationPath, overwrite: true);
            return new CatalogueFeedDownload(length, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The snapshot download timed out.", ex);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private static bool TryBuildUri(string? value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!))
        {
            return false;
        }
        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    // Null when the body is longer than `limit`.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int limit, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
