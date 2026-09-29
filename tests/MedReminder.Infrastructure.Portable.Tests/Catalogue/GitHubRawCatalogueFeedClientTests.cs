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

// HTTP behaviour of the remote AIFA feed client against a fake handler:
// status codes, caps, redirects, timeouts, hashing and file handling.
public sealed class GitHubRawCatalogueFeedClientTests : IDisposable
{
    private const string ManifestJson =
        """{"version":"202610","file":"aifa-202610.zip","generated":"2026-10-02T03:00:00+00:00","csv_count":2}""";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "medreminder-feed-client-" + Guid.NewGuid().ToString("N"));

    public GitHubRawCatalogueFeedClientTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Destination => Path.Combine(_directory, "aifa-202610.zip");

    private static readonly CatalogueFeedManifest Manifest = new("202610", "aifa-202610.zip", null, null, null);

    [Fact]
    public async Task GetLatestAsync_parses_the_manifest_from_the_configured_url()
    {
        var handler = new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson));
        using var client = Build(handler);

        var manifest = await client.GetLatestAsync(CancellationToken.None);

        manifest!.Version.Should().Be("202610");
        handler.Urls.Should().Equal(CatalogueFeedOptions.DefaultManifestUrl);
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

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_an_invalid_manifest()
    {
        using var client = Build(new Endpoint(_ => Text(HttpStatusCode.OK, """{"version":"latest"}""")));

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_an_oversized_manifest_without_length()
    {
        var body = "{\"version\":\"202610\",\"pad\":\"" + new string('x', 5000) + "\"}";
        using var client = Build(new Endpoint(_ => Streamed(HttpStatusCode.OK, Encoding.UTF8.GetBytes(body))));

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_a_network_error()
    {
        using var client = Build(new Endpoint(_ => throw new HttpRequestException("DNS failure")));

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_returns_null_on_timeout()
    {
        using var client = Build(new SlowEndpoint(), o => o.ManifestTimeoutSeconds = 1);

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetLatestAsync_refuses_a_plain_http_url_for_a_remote_host()
    {
        var handler = new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson));
        using var client = Build(handler, o => o.ManifestUrl = "http://example.com/latest.json");

        (await client.GetLatestAsync(CancellationToken.None)).Should().BeNull();
        handler.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestAsync_accepts_plain_http_on_loopback()
    {
        using var client = Build(
            new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson)),
            o => o.ManifestUrl = "http://127.0.0.1:8080/latest.json");

        (await client.GetLatestAsync(CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_writes_the_file_and_returns_length_and_sha256()
    {
        var payload = RandomNumberGenerator.GetBytes(200_000);
        var handler = new Endpoint(_ => Bytes(HttpStatusCode.OK, payload));
        using var client = Build(handler);

        var download = await client.DownloadAsync(Manifest, Destination, CancellationToken.None);

        download.Length.Should().Be(payload.Length);
        download.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(payload)));
        (await File.ReadAllBytesAsync(Destination)).Should().Equal(payload);
        File.Exists(Destination + ".part").Should().BeFalse();
        handler.Urls.Should().Equal(
            "https://raw.githubusercontent.com/vger70/MedReminder/main/data/aifa-202610.zip");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task DownloadAsync_throws_on_a_non_success_status(HttpStatusCode status)
    {
        using var client = Build(new Endpoint(_ => Bytes(status, [1, 2, 3])));

        var act = () => client.DownloadAsync(Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_rejects_a_declared_length_above_the_cap()
    {
        using var client = Build(new Endpoint(_ => Bytes(HttpStatusCode.OK, new byte[2048])), o => o.MaxDownloadBytes = 1024);

        var act = () => client.DownloadAsync(Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Destination + ".part").Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_rejects_a_stream_above_the_cap_without_a_declared_length()
    {
        using var client = Build(new Endpoint(_ => Streamed(HttpStatusCode.OK, new byte[200_000])), o => o.MaxDownloadBytes = 100_000);

        var act = () => client.DownloadAsync(Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Destination).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_reports_a_timeout_as_TimeoutException()
    {
        using var client = Build(new SlowEndpoint(), o => o.DownloadTimeoutSeconds = 1);

        var act = () => client.DownloadAsync(Manifest, Destination, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task DownloadAsync_propagates_caller_cancellation()
    {
        using var client = Build(new SlowEndpoint());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => client.DownloadAsync(Manifest, Destination, cts.Token);

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
