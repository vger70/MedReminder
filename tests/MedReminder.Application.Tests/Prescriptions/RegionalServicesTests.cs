using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Prescriptions;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

// Regional prescription services (docs/prompt/
// PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1–§3.3, §6).
public sealed class RegionalServicesTests
{
    private const string ListJson =
        "{\"country\":\"IT\",\"listDate\":\"2026-10-04\",\"generated\":\"2026-10-04T05:00:00+00:00\",\"services\":[" +
        "{\"regionCode\":\"12\",\"region\":\"Lazio\",\"service\":\"Salute Lazio\",\"webUrl\":\"https://www.salutelazio.it/\"," +
        "\"iosAppUrl\":\"https://apps.apple.com/it/app/salutelazio/id1201847471\",\"androidAppUrl\":null," +
        "\"signIn\":[\"SPID\",\"CIE\",\"TS-CNS\"],\"showsPrescriptions\":true,\"familyDelegation\":true," +
        "\"verifiedOn\":\"2026-10-04\",\"note\":\"unknown fields are ignored\"}," +
        "{\"regionCode\":\"05\",\"region\":\"Veneto\",\"service\":\"Sanità km zero Ricette\"," +
        "\"webUrl\":\"https://www.sanitakmzero.it/\",\"signIn\":[\"SPID\"],\"showsPrescriptions\":false," +
        "\"familyDelegation\":false,\"verifiedOn\":\"2026-09-30\"}]}";

    private readonly ApplicationTestScope _scope = new();

    private static RegionalServicesList Parse(string json = ListJson)
    {
        RegionalServicesFeedParser.TryParseList(json, out var list, out var error).Should().BeTrue(error);
        return list!;
    }

    [Fact]
    public void A_valid_list_is_parsed()
    {
        var list = Parse();

        list.ListDate.Should().Be(new DateOnly(2026, 10, 4));
        list.Count.Should().Be(2);
        var lazio = list.Find("12")!;
        lazio.Service.Should().Be("Salute Lazio");
        lazio.WebUrl.Should().Be(new Uri("https://www.salutelazio.it/"));
        lazio.IosAppUrl!.Host.Should().Be("apps.apple.com");
        lazio.AndroidAppUrl.Should().BeNull();
        lazio.SignIn.Should().Equal(SignInMethod.Spid, SignInMethod.Cie, SignInMethod.TsCns);
        lazio.ShowsPrescriptions.Should().BeTrue();
        lazio.FamilyDelegation.Should().BeTrue();
        list.Find("05")!.IosAppUrl.Should().BeNull("an entry may have webUrl only");
    }

    [Theory]
    [InlineData("\"country\":\"IT\"", "\"country\":\"FR\"")]
    [InlineData("\"regionCode\":\"05\"", "\"regionCode\":\"12\"")]
    [InlineData("\"regionCode\":\"05\"", "\"regionCode\":\"04\"")]
    [InlineData("\"webUrl\":\"https://www.sanitakmzero.it/\"", "\"webUrl\":\"http://www.sanitakmzero.it/\"")]
    [InlineData("\"webUrl\":\"https://www.sanitakmzero.it/\"", "\"webUrl\":\"https://www.sanitakmzero.it:8443/\"")]
    [InlineData("\"androidAppUrl\":null", "\"androidAppUrl\":\"javascript:alert(1)\"")]
    [InlineData("\"signIn\":[\"SPID\"]", "\"signIn\":[\"Password\"]")]
    [InlineData("\"signIn\":[\"SPID\"]", "\"signIn\":[]")]
    [InlineData("\"showsPrescriptions\":false", "\"showsPrescriptions\":\"no\"")]
    [InlineData("\"verifiedOn\":\"2026-09-30\"", "\"verifiedOn\":\"30/09/2026\"")]
    [InlineData("\"service\":\"Salute Lazio\",", "")]
    public void An_invalid_list_is_rejected(string from, string to)
    {
        ListJson.Should().Contain(from);
        RegionalServicesFeedParser.TryParseList(ListJson.Replace(from, to), out var list, out var error)
            .Should().BeFalse();
        list.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_manifest_needs_the_regional_services_prefix()
    {
        const string manifest = "{\"country\":\"IT\",\"version\":\"20261004\",\"file\":\"regional-services-20261004.json\"," +
            "\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"size\":10}";
        RegionalServicesFeedParser.TryParseManifest(manifest, out var parsed, out _).Should().BeTrue();
        parsed!.ListDate.Should().Be(new DateOnly(2026, 10, 4));
        RegionalServicesFeedParser.TryParseManifest(manifest.Replace("regional-services-", "shortages-"), out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void The_downloaded_list_wins_over_the_shipped_copy_only_when_not_older()
    {
        var shipped = Parse();
        var newer = Parse(ListJson.Replace("2026-10-04\",\"generated", "2026-11-02\",\"generated"));
        var older = Parse(ListJson.Replace("2026-10-04\",\"generated", "2026-09-01\",\"generated"));
        var same = Parse();

        RegionalServicesListChoice.Newest(newer, shipped).Should().BeSameAs(newer);
        RegionalServicesListChoice.Newest(older, shipped).Should().BeSameAs(shipped);
        RegionalServicesListChoice.Newest(same, shipped).Should().BeSameAs(same);
        RegionalServicesListChoice.Newest(null, shipped).Should().BeSameAs(shipped);
        RegionalServicesListChoice.Newest(newer, null).Should().BeSameAs(newer);
        RegionalServicesListChoice.Newest(null, null).Should().BeNull();
    }

    [Fact]
    public async Task The_refresher_stores_a_newer_list()
    {
        var store = new MemoryStore(null);
        var feed = new MemoryFeed(ListJson, "20261004");
        var refresher = new RegionalServicesRefresher(feed, store, NullLogger<RegionalServicesRefresher>.Instance);

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Updated);
        store.Load()!.Count.Should().Be(2);
        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.UpToDate);
    }

    [Theory]
    [InlineData("IT", true)]
    [InlineData("", true)]
    [InlineData("FR", false)]
    [InlineData("EU", false)]
    public void The_list_is_refreshed_with_italy_only(string country, bool expected)
    {
        CatalogueFeedSelection.IncludesRegionalServices(country, new CatalogueFeedOptions()).Should().Be(expected);
        CatalogueFeedSelection.IncludesRegionalServices("IT", new CatalogueFeedOptions { RegionalServicesEnabled = false })
            .Should().BeFalse();
    }

    [Fact]
    public void The_query_returns_nothing_outside_italy_without_a_region_or_without_an_entry()
    {
        var query = new RegionalServiceForProfileQuery(_scope.ProfileSettings, new MemoryStore(Parse()));

        query.Get("FR").Availability.Should().Be(RegionalServiceAvailability.NotItaly);
        query.Get("IT").Availability.Should().Be(RegionalServiceAvailability.NoRegion);

        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.Region] = "15" });
        var none = query.Get("IT");
        none.Availability.Should().Be(RegionalServiceAvailability.NoEntry);
        none.Region.Should().Be("15");
        none.Service.Should().BeNull();

        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.Region] = "12" });
        var found = query.Get("IT");
        found.Availability.Should().Be(RegionalServiceAvailability.Found);
        found.Service!.Service.Should().Be("Salute Lazio");
        query.Get("FR").Service.Should().BeNull("the reference country is checked first");

        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.Region] = "99" });
        query.Get("IT").Availability.Should().Be(RegionalServiceAvailability.NoRegion, "an unknown code is no region");

        _scope.ProfileSettings.Write(new Dictionary<string, string?> { [ProfileSetting.Region] = "12" });
        new RegionalServiceForProfileQuery(_scope.ProfileSettings, new MemoryStore(null)).Get("IT").Availability
            .Should().Be(RegionalServiceAvailability.NoEntry, "no list at all");
    }

    [Theory]
    [InlineData("https://www.salutelazio.it/", true)]
    [InlineData("https://WWW.SALUTELAZIO.IT/", true)]
    [InlineData("https://apps.apple.com/it/app/salutelazio/id1201847471", true)]
    [InlineData("http://www.salutelazio.it/", false)]
    [InlineData("https://www.salutelazio.it.example.org/", false)]
    [InlineData("https://example.org/", false)]
    [InlineData("https://user:pw@www.salutelazio.it/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("www.salutelazio.it", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_launcher_opens_only_https_urls_of_the_list(string? url, bool opened)
    {
        var opener = new RecordingOpener();
        var log = new ListLogger();
        var launcher = new RegionalServiceLinkLauncher(new MemoryStore(Parse()), opener, log);

        launcher.Open("12", url).Should().Be(opened);

        opener.Opened.Should().HaveCount(opened ? 1 : 0);
        if (opened)
        {
            opener.Opened[0].AbsoluteUri.Should().Be(new Uri(url!).AbsoluteUri, "nothing is added to the URL");
            log.Lines.Should().ContainSingle().Which.Should().Be("Regional service opened: 12");
        }
        else
        {
            log.Lines.Should().ContainSingle().Which.Should().StartWith("Regional service link refused for region 12");
            if (!string.IsNullOrEmpty(url)) log.Lines[0].Should().NotContain(url, "the URL is not logged");
        }
    }

    [Fact]
    public void A_browser_that_cannot_start_is_reported_not_thrown()
    {
        var log = new ListLogger();
        var launcher = new RegionalServiceLinkLauncher(new MemoryStore(Parse()), new RecordingOpener(fail: true), log);

        launcher.Open("12", "https://www.salutelazio.it/").Should().BeFalse();
        log.Lines.Should().ContainSingle().Which.Should().Contain("could not be opened for region 12");
    }

    [Fact]
    public async Task The_region_is_recorded_once_and_replicated()
    {
        _scope.EnableSync();
        var update = new UpdateProfileRegion(_scope.ProfileSettings, _scope.Operations, _scope.Uow);

        await update.ExecuteAsync(" 12 ", default);
        await update.ExecuteAsync("12", default);

        _scope.ProfileSettings.Read()[ProfileSetting.Region].Should().Be("12");
        (await _scope.SyncOperations.ListAllAsync(default))
            .Count(o => o.Type == "ProfileSettingChanged" && o.Payload.Contains("\"Region\""))
            .Should().Be(1);
    }

    [Theory]
    [InlineData("04")]
    [InlineData("99")]
    [InlineData("Lazio")]
    public async Task An_unknown_region_is_refused(string region)
    {
        var update = new UpdateProfileRegion(_scope.ProfileSettings, _scope.Operations, _scope.Uow);
        var settings = new UpdateNotificationSettings(_scope.ProfileSettings, _scope.Registers, _scope.Operations, _scope.Uow);

        await FluentActions.Invoking(() => update.ExecuteAsync(region, default)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => settings.ExecuteAsync("", "", "", default, region: region))
            .Should().ThrowAsync<ArgumentException>();
        _scope.ProfileSettings.Read()[ProfileSetting.Region].Should().BeEmpty();
    }

    [Fact]
    public async Task The_settings_dialog_saves_and_clears_the_region()
    {
        var settings = new UpdateNotificationSettings(_scope.ProfileSettings, _scope.Registers, _scope.Operations, _scope.Uow);

        await settings.ExecuteAsync("", "", "", default, region: "20");
        _scope.ProfileSettings.Read()[ProfileSetting.Region].Should().Be("20");

        await settings.ExecuteAsync("", "", "", default);
        _scope.ProfileSettings.Read()[ProfileSetting.Region].Should().Be("20", "null leaves the region as it is");

        await settings.ExecuteAsync("", "", "", default, region: "");
        _scope.ProfileSettings.Read()[ProfileSetting.Region].Should().BeEmpty();
    }

    private sealed class MemoryStore(RegionalServicesList? list) : IRegionalServicesListStore
    {
        private RegionalServicesList? _list = list;
        private string? _sha;

        public RegionalServicesList? Load() => _list;

        public string? StoredSha256() => _list is null ? null : _sha;

        public void Save(byte[] listJson)
        {
            RegionalServicesFeedParser.TryParseList(Encoding.UTF8.GetString(listJson), out _list, out _);
            _sha = Convert.ToHexStringLower(SHA256.HashData(listJson));
        }
    }

    private sealed class MemoryFeed(string json, string version) : IRegionalServicesFeedClient
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(json);

        public Task<DatedFeedManifest?> GetManifestAsync(CancellationToken cancellationToken)
            => Task.FromResult<DatedFeedManifest?>(new DatedFeedManifest(version,
                RegionalServicesFeedParser.FilePrefix + version + ".json",
                Convert.ToHexStringLower(SHA256.HashData(_bytes)), _bytes.LongLength));

        public Task<byte[]> DownloadAsync(DatedFeedManifest manifest, CancellationToken cancellationToken)
            => Task.FromResult(_bytes);
    }

    private sealed class RecordingOpener(bool fail = false) : IUrlOpener
    {
        public List<Uri> Opened { get; } = [];

        public void Open(Uri uri)
        {
            if (fail) throw new InvalidOperationException("no browser");
            Opened.Add(uri);
        }
    }

    private sealed class ListLogger : ILogger<RegionalServiceLinkLauncher>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }
}
