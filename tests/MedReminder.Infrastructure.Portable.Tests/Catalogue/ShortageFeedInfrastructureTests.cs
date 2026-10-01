using System.Net;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.Infrastructure.Portable.Tests.Catalogue;

// Shortage feed (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3): the HTTP
// client against a fake handler and the file store on disk.
public sealed class ShortageFeedInfrastructureTests : IDisposable
{
    private const string ListJson =
        "{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[" +
        "{\"aic\":\"045348036\",\"start\":\"2025-07-28\",\"expectedEnd\":null,\"equivalent\":true,\"reason\":\"production\"}]}";

    private const string ManifestJson =
        "{\"country\":\"IT\",\"version\":\"20260929\",\"file\":\"shortages-20260929.json\"," +
        "\"sha256\":\"5d5e94ceadacf716fd14fca40977382728a47777c6d29825f1657e30c0f6b5c3\",\"size\":120}";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-shortages-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task The_manifest_and_the_list_are_read_from_the_shortages_folder()
    {
        var endpoint = new Endpoint(request => request.RequestUri!.AbsolutePath.EndsWith("latest.json")
            ? Text(HttpStatusCode.OK, ManifestJson)
            : Text(HttpStatusCode.OK, ListJson));
        var client = Build(endpoint);

        var manifest = await client.GetManifestAsync(default);
        var bytes = await client.DownloadAsync(manifest!, default);

        manifest!.Version.Should().Be("20260929");
        Encoding.UTF8.GetString(bytes).Should().Be(ListJson);
        endpoint.Urls.Should().Equal(
            "https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/shortages/latest.json",
            "https://raw.githubusercontent.com/vger70/MedReminder/main/data/it/shortages/shortages-20260929.json");
    }

    [Fact]
    public async Task An_error_status_or_an_invalid_manifest_gives_no_manifest()
    {
        (await Build(new Endpoint(_ => Text(HttpStatusCode.NotFound, ""))).GetManifestAsync(default)).Should().BeNull();
        (await Build(new Endpoint(_ => Text(HttpStatusCode.OK, "{}"))).GetManifestAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task A_list_above_the_limit_is_refused()
    {
        var client = Build(new Endpoint(_ => Text(HttpStatusCode.OK, ListJson)), o => o.ShortagesMaxDownloadBytes = 10);
        ShortageFeedParser.TryParseManifest(ManifestJson, out var manifest, out _).Should().BeTrue();

        await FluentActions.Invoking(() => client.DownloadAsync(manifest!, default))
            .Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task A_plain_http_url_is_refused()
    {
        var endpoint = new Endpoint(_ => Text(HttpStatusCode.OK, ManifestJson));
        var client = Build(endpoint, o => o.BaseUrl = "http://example.org/data/");

        (await client.GetManifestAsync(default)).Should().BeNull();
        endpoint.Urls.Should().BeEmpty();
    }

    [Fact]
    public void The_store_saves_and_reloads_the_list_and_ignores_a_damaged_file()
    {
        var path = Path.Combine(_directory, "catalogue", "shortages", "shortages-it.json");
        var store = new JsonFileShortageListStore(path);
        store.Load().Should().BeNull();

        store.Save(Encoding.UTF8.GetBytes(ListJson));

        store.Load()!.Find("045348036").Should().NotBeNull();
        new JsonFileShortageListStore(path).Load()!.Count.Should().Be(1);
        File.Exists(path + ".tmp").Should().BeFalse();

        File.WriteAllText(path, "{ damaged");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        store.Load().Should().BeNull();
    }

    private static GitHubRawShortageFeedClient Build(HttpMessageHandler handler, Action<CatalogueFeedOptions>? configure = null)
    {
        var options = new CatalogueFeedOptions { Enabled = true };
        configure?.Invoke(options);
        return new GitHubRawShortageFeedClient(handler, new StaticOptions(options),
            NullLogger<GitHubRawShortageFeedClient>.Instance);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Endpoint(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }

    private sealed class StaticOptions(CatalogueFeedOptions value) : IOptionsMonitor<CatalogueFeedOptions>
    {
        public CatalogueFeedOptions CurrentValue => value;

        public CatalogueFeedOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<CatalogueFeedOptions, string?> listener) => null;
    }
}
