using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.Infrastructure.Portable.Tests.Catalogue;

// HTTP behaviour of the remote catalogue feed client against a fake
// handler: URLs per feed, status codes, caps, redirects, timeouts,
// hashing and file handling.
public sealed class GitHubRawCatalogueFeedClientTests : IDisposable
{
    private const string ManifestJson =
        """{"version":"202610","file":"aifa-202610.zip","generated":"2026-10-02T03:00:00+00:00","csv_count":2}""";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "medreminder-feed-client-" + Guid.NewGuid().ToString("N"));

    public GitHubRawCatalogueFeedClientTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Destination => Path.Combine(_directory, "aifa-202610.zip");

    private static readonly CatalogueFeedDescriptor Italy = CatalogueFeedDescriptor.Italy;

    private static readonly CatalogueFeedManifest Manifest = new("202610", "aifa-202610.zip", null, null, null);

    [Theory]
    [InlineData("IT", "it", "aifa")]
    [InlineData("EU", "eu", "ema-epar")]
    [InlineData("ES", "es", "aemps")]
    [InlineData("FR", "fr", "bdpm")]
    public async Task Builds_the_manifest_and_archive_urls_of_each_feed(string country, string folder, string prefix)
    {
        var feed = CatalogueFeedDescriptor.All.Single(f => f.Country.Value == country);
        var manifestJson = $$"""{"country":"{{country}}","version":"202610","file":"{{prefix}}-202610.zip"}""";
        var handler = new Endpoint(request => request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
            ? Text(HttpStatusCode.OK, manifestJson)
            : Bytes(HttpStatusCode.OK, [1, 2, 3]));
        using var client = Build(handler);

        var manifest = await client.GetLatestAsync(feed, CancellationToken.None);
        await client.DownloadAsync(feed, manifest!, Path.Combine(_directory, "archive.zip"), CancellationToken.None);

        handler.Urls.Should().Equal(
            $"https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/{folder}/latest.json",
            $"https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/{folder}/{prefix}-202610.zip");
    }

    [Fact]
    public async Task Builds_the_urls_from_a_base_url_without_trailing_slash()
    {
        var handler = new Endpoint(_ => Text(HttpStatusCode.OK, """{"version":"202610"}"""));
        using var client = Build(handler, o => o.BaseUrl = "https://example.org/feeds");

        await client.GetLatestAsync(CatalogueFeedDescriptor.Spain, CancellationToken.None);

        handler.Urls.Should().Equal("https://example.org/feeds/es/latest.json");
    }

    [Fact]
    public async Task The_Italian_url_overrides_apply_to_Italy_only()
    {
        var handler = new Endpoint(request => request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
            ? Text(HttpStatusCode.OK, """{"version":"202610"}""")
            : Bytes(HttpStatusCode.OK, [1, 2, 3]));
        using var client = Build(handler, o =>
        {
            o.ManifestUrl = "https://mirror.example.org/aifa/latest.json";
            o.SnapshotUrlTemplate = "https://mirror.example.org/aifa/{version}.zip";
        });

        await client.GetLatestAsync(Italy, CancellationToken.None);
        await client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);
        await client.GetLatestAsync(CatalogueFeedDescriptor.France, CancellationToken.None);

        handler.Urls.Should().Equal(
            "https://mirror.example.org/aifa/latest.json",
            "https://mirror.example.org/aifa/202610.zip",
            "https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/fr/latest.json");
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_for_a_manifest_of_another_feed()
    {
        using var client = Build(new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson)));

        (await client.GetLatestAsync(CatalogueFeedDescriptor.Spain, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DownloadAsync_applies_the_cap_of_the_feed()
    {
        var manifest = new CatalogueFeedManifest("202610", "aemps-202610.zip", null, null, null);
        using var client = Build(
            new Endpoint(_ => Bytes(HttpStatusCode.OK, new byte[2048])),
            o => o.Feeds["ES"].MaxDownloadBytes = 1024);

        var act = () => client.DownloadAsync(CatalogueFeedDescriptor.Spain, manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        (await client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None)).Length.Should().Be(2048);
    }

    [Fact]
    public async Task DownloadAsync_refuses_a_feed_without_configuration()
    {
        var manifest = new CatalogueFeedManifest("202610", "bdpm-202610.zip", null, null, null);
        using var client = Build(new Endpoint(_ => Bytes(HttpStatusCode.OK, [1])), o => o.Feeds.Remove("FR"));

        var act = () => client.DownloadAsync(CatalogueFeedDescriptor.France, manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task GetLatestAsync_parses_the_manifest_from_the_configured_url()
    {
        var handler = new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson));
        using var client = Build(handler);

        var manifest = await client.GetLatestAsync(Italy, CancellationToken.None);

        manifest!.Version.Should().Be("202610");
        handler.Urls.Should().Equal("https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/latest.json");
        handler.UserAgents.Single().Should().StartWith("MedReminder/");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task GetLatestAsync_returns_null_on_a_non_success_status(HttpStatusCode status)
    {
        using var client = Build(new Endpoint(_ =>
        {
            var response = Text(status, ManifestJson);
            response.Headers.Location = new Uri("https://example.invalid/latest.json");
            return response;
        }));

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_an_invalid_manifest()
    {
        using var client = Build(new Endpoint(_ => Text(HttpStatusCode.OK, """{"version":"latest"}""")));

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_an_oversized_manifest_without_length()
    {
        var body = "{\"version\":\"202610\",\"pad\":\"" + new string('x', 5000) + "\"}";
        using var client = Build(new Endpoint(_ => Streamed(HttpStatusCode.OK, Encoding.UTF8.GetBytes(body))));

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_a_network_error()
    {
        using var client = Build(new Endpoint(_ => throw new HttpRequestException("DNS failure")));

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_when_the_body_read_fails()
    {
        using var client = Build(new Endpoint(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) }));

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_timeout()
    {
        using var client = Build(new SlowEndpoint(), o => o.ManifestTimeoutSeconds = 1);

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_refuses_a_plain_http_url_for_a_remote_host()
    {
        var handler = new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson));
        using var client = Build(handler, o => o.ManifestUrl = "http://example.com/latest.json");

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().BeNull();
        handler.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_accepts_plain_http_on_loopback()
    {
        using var client = Build(
            new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson)),
            o => o.ManifestUrl = "http://127.0.0.1:8080/latest.json");

        (await client.GetLatestAsync(Italy, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_writes_the_file_and_returns_length_and_sha256()
    {
        var payload = RandomNumberGenerator.GetBytes(200_000);
        var handler = new Endpoint(_ => Bytes(HttpStatusCode.OK, payload));
        using var client = Build(handler);

        var download = await client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);

        download.Length.Should().Be(payload.Length);
        download.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(payload)));
        (await File.ReadAllBytesAsync(Destination)).Should().Equal(payload);
        File.Exists(Destination + ".part").Should().BeFalse();
        handler.Urls.Should().Equal(
            "https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/aifa-202610.zip");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task DownloadAsync_throws_on_a_non_success_status(HttpStatusCode status)
    {
        using var client = Build(new Endpoint(_ => Bytes(status, [1, 2, 3])));

        var act = () => client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_rejects_a_declared_length_above_the_cap()
    {
        using var client = Build(new Endpoint(_ => Bytes(HttpStatusCode.OK, new byte[2048])), o => o.Feeds["IT"].MaxDownloadBytes = 1024);

        var act = () => client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Destination + ".part").Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_rejects_a_stream_above_the_cap_without_a_declared_length()
    {
        using var client = Build(new Endpoint(_ => Streamed(HttpStatusCode.OK, new byte[200_000])), o => o.Feeds["IT"].MaxDownloadBytes = 100_000);

        var act = () => client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_reports_a_timeout_as_TimeoutException()
    {
        using var client = Build(new SlowEndpoint(), o => o.DownloadTimeoutSeconds = 1);

        var act = () => client.DownloadAsync(Italy, Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task DownloadAsync_propagates_caller_cancellation()
    {
        using var client = Build(new SlowEndpoint());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => client.DownloadAsync(Italy, Manifest, Destination, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static GitHubRawCatalogueFeedClient Build(
        HttpMessageHandler handler, Action<CatalogueFeedOptions>? configure = null)
    {
        var options = new CatalogueFeedOptions { Enabled = true };
        configure?.Invoke(options);
        return new GitHubRawCatalogueFeedClient(
            handler, new StaticOptions(options), NullLogger<GitHubRawCatalogueFeedClient>.Instance);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) };

    // Content without Content-Length, so only the streaming cap applies.
    private static HttpResponseMessage Streamed(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new StreamContent(new NonSeekableStream(body)) };

    private sealed class Endpoint(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private sealed class SlowEndpoint : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticOptions(CatalogueFeedOptions value) : IOptionsMonitor<CatalogueFeedOptions>
    {
        public CatalogueFeedOptions CurrentValue => value;

        public CatalogueFeedOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CatalogueFeedOptions, string?> listener) => null;
    }

    // Simulates a connection reset after the headers arrived.
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
