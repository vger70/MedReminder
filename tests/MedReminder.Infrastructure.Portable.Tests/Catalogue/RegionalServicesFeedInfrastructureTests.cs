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

// Regional services feed (docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md
// §3.1): the HTTP client against a fake handler, the file store on disk
// and the copy shipped in the assembly.
public sealed class RegionalServicesFeedInfrastructureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-regional-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "catalogue", "regional-services", "regional-services-it.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string ListJson(string date, string service = "Salute Lazio") =>
        "{\"country\":\"IT\",\"listDate\":\"" + date + "\",\"services\":[{\"regionCode\":\"12\",\"region\":\"Lazio\"," +
        "\"service\":\"" + service + "\",\"webUrl\":\"https://www.salutelazio.it/\",\"signIn\":[\"SPID\"]," +
        "\"showsPrescriptions\":true,\"familyDelegation\":true,\"verifiedOn\":\"2026-10-04\"}]}";

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task The_manifest_and_the_list_are_read_from_the_regional_services_folder()
    {
        var list = ListJson("2026-10-04");
        var manifest = "{\"country\":\"IT\",\"version\":\"20261004\",\"file\":\"regional-services-20261004.json\"," +
            $"\"sha256\":\"{Sha(list)}\",\"size\":{Encoding.UTF8.GetByteCount(list)}}}";
        var endpoint = new Endpoint(request => request.RequestUri!.AbsolutePath.EndsWith("latest.json")
            ? Text(HttpStatusCode.OK, manifest)
            : Text(HttpStatusCode.OK, list));
        var client = Build(endpoint);

        var read = await client.GetManifestAsync(default);
        var bytes = await client.DownloadAsync(read!, default);

        Encoding.UTF8.GetString(bytes).Should().Be(list);
        endpoint.Urls.Should().Equal(
            "https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/regional-services/latest.json",
            "https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/regional-services/regional-services-20261004.json");
    }

    [Fact]
    public void The_shipped_copy_is_embedded_and_valid()
    {
        var store = new JsonFileRegionalServicesListStore(new Location(_directory));

        var list = store.Load();

        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterThan(0);
        list.Find("12")!.WebUrl.Scheme.Should().Be("https");
        store.StoredSha256().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void The_shipped_copy_is_used_until_a_newer_list_is_downloaded()
    {
        var shipped = Encoding.UTF8.GetBytes(ListJson("2026-10-04", "Shipped"));
        var store = new JsonFileRegionalServicesListStore(StorePath, () => shipped);
        store.Load()!.Find("12")!.Service.Should().Be("Shipped");
        store.StoredSha256().Should().Be(Convert.ToHexStringLower(SHA256.HashData(shipped)));

        var downloaded = ListJson("2026-11-02", "Downloaded");
        store.Save(Encoding.UTF8.GetBytes(downloaded));

        store.Load()!.Find("12")!.Service.Should().Be("Downloaded");
        store.StoredSha256().Should().Be(Sha(downloaded));
    }

    [Fact]
    public void A_newer_shipped_copy_wins_over_an_older_download_and_the_same_date_keeps_the_download()
    {
        var older = ListJson("2026-09-01", "Downloaded");
        new JsonFileRegionalServicesListStore(StorePath, () => null).Save(Encoding.UTF8.GetBytes(older));

        var newer = new JsonFileRegionalServicesListStore(StorePath,
            () => Encoding.UTF8.GetBytes(ListJson("2026-10-04", "Shipped")));
        newer.Load()!.Find("12")!.Service.Should().Be("Shipped");

        var same = new JsonFileRegionalServicesListStore(StorePath,
            () => Encoding.UTF8.GetBytes(ListJson("2026-09-01", "Shipped")));
        same.Load()!.Find("12")!.Service.Should().Be("Downloaded");
        same.StoredSha256().Should().Be(Sha(older));
    }

    [Fact]
    public void Without_any_valid_list_the_store_is_empty()
    {
        var store = new JsonFileRegionalServicesListStore(StorePath, () => Encoding.UTF8.GetBytes("{ damaged"));

        store.Load().Should().BeNull();
        store.StoredSha256().Should().BeNull();
    }

    private static GitHubRawRegionalServicesFeedClient Build(HttpMessageHandler handler)
        => new(handler, new StaticOptions(new CatalogueFeedOptions { Enabled = true }),
            NullLogger<GitHubRawRegionalServicesFeedClient>.Instance);

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

    private sealed class Location(string directory) : MedReminder.Application.Abstractions.IAppDataLocation
    {
        public string DataDirectory => directory;
    }
}
