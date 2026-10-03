using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Overview;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// Equivalents feed, refresh, query and Codifa link
// (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md).
public class EquivalenceFeedTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    // Two groups from tests/fixtures/catalogue/aifa-equivalents-sample.csv,
    // as scripts/feeds/aifa_equivalents.py publishes them.
    private const string ListJson =
        "{\"country\":\"IT\",\"source\":\"AIFA\",\"listDate\":\"2026-09-15\",\"generated\":\"2026-10-01T00:00:00+00:00\"," +
        "\"groups\":[" +
        "{\"code\":\"12A\",\"ingredient\":\"NIFEDIPINA\",\"reference\":\"14 UNITA' 30 MG - USO ORALE A RILASCIO MODIFICATO\"," +
        "\"atc\":\"C08CA05\",\"referencePrice\":512,\"members\":[" +
        "{\"aic\":\"026622050\",\"name\":\"ADALAT CRONO\",\"package\":\"30 MG 14 COMPRESSE\",\"holder\":\"BAYER S.P.A.\",\"price\":620,\"difference\":108,\"note\":null}," +
        "{\"aic\":\"035831015\",\"name\":\"NIFEDIPINA DOC*\",\"package\":\"30 MG 14 COMPRESSE\",\"holder\":\"DOC GENERICI S.R.L.\",\"price\":512,\"difference\":0,\"note\":\"*non sostituibile con Adalat Crono\"}," +
        "{\"aic\":\"037248022\",\"name\":\"NIFEDIPINA EG\",\"package\":\"30 MG 14 COMPRESSE\",\"holder\":\"EG S.P.A.\",\"price\":512,\"difference\":0,\"note\":null}]}," +
        "{\"code\":\"341\",\"ingredient\":\"AMLODIPINA\",\"reference\":\"28 UNITA' 5 MG - USO ORALE\",\"atc\":\"C08CA01\",\"referencePrice\":295,\"members\":[" +
        "{\"aic\":\"002783013\",\"name\":\"NORVASC\",\"package\":\"5 MG 28 COMPRESSE\",\"holder\":\"PFIZER ITALIA S.R.L.\",\"price\":549,\"difference\":254,\"note\":null}," +
        "{\"aic\":\"035123037\",\"name\":\"AMLODIPINA TEVA\",\"package\":\"5 MG 28 COMPRESSE\",\"holder\":\"TEVA ITALIA S.R.L.\",\"price\":null,\"difference\":null,\"note\":null}]}]}";

    private static string Manifest(string json, string version = "20260915", long? size = null, string? sha = null)
        => $"{{\"country\":\"IT\",\"version\":\"{version}\",\"file\":\"equivalents-{version}.json\"," +
           $"\"generated\":\"2026-10-01T00:00:00+00:00\",\"sha256\":\"{sha ?? Sha(json)}\"," +
           $"\"size\":{size ?? Encoding.UTF8.GetByteCount(json)},\"rows\":{{\"groups\":2,\"packages\":5}}}}";

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    private static EquivalenceList Parsed()
    {
        EquivalenceFeedParser.TryParseList(ListJson, out var list, out var error).Should().BeTrue(error);
        return list!;
    }

    [Fact]
    public void A_valid_list_is_parsed_with_prices_in_euros_and_notes_verbatim()
    {
        var list = Parsed();

        list.ListDate.Should().Be(new DateOnly(2026, 9, 15));
        list.GroupCount.Should().Be(2);
        list.PackageCount.Should().Be(5);
        var group = list.FindGroup("035831015")!;
        group.Code.Should().Be("12A");
        group.ReferencePrice.Should().Be(5.12m);
        group.AtcCode.Should().Be("C08CA05");
        group.Members.Single(m => m.Code == "035831015").Note.Should().Be("*non sostituibile con Adalat Crono");
        group.Members.Single(m => m.Code == "026622050").Should().Be(new EquivalentPackage(
            "026622050", "ADALAT CRONO", "30 MG 14 COMPRESSE", "BAYER S.P.A.", 6.20m, 1.08m, null));
        list.FindGroup("035123037")!.Members.Single(m => m.Code == "035123037").PublicPrice.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"country\":\"FR\",\"listDate\":\"2026-09-15\",\"groups\":[]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"15/09/2026\",\"groups\":[]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\"}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\",\"groups\":[{\"members\":[{\"aic\":\"026622050\"}]}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\",\"groups\":[{\"code\":\"12A\",\"members\":[]}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\",\"groups\":[{\"code\":\"12A\",\"members\":[{\"aic\":\"2783013\"}]}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\",\"groups\":[{\"code\":\"12A\",\"members\":[{\"aic\":\"026622050\",\"price\":\"5,12\"}]}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-15\",\"groups\":[{\"code\":\"12A\",\"members\":[{\"aic\":\"026622050\",\"price\":5.12}]}]}")]
    [InlineData("not json")]
    public void An_invalid_list_is_rejected(string json)
    {
        EquivalenceFeedParser.TryParseList(json, out var list, out var error).Should().BeFalse();
        list.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_manifest_must_name_its_file_and_carry_a_hash_and_a_size()
    {
        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson), out var manifest, out _).Should().BeTrue();
        manifest!.ListDate.Should().Be(new DateOnly(2026, 9, 15));

        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson).Replace("equivalents-20260915", "shortages-20260915"), out _, out _)
            .Should().BeFalse();
        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson, version: "202609"), out _, out _).Should().BeFalse();
        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson, sha: "abc"), out _, out _).Should().BeFalse();
        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson, size: 0), out _, out _).Should().BeFalse();
        EquivalenceFeedParser.TryParseManifest(Manifest(ListJson).Replace("\"IT\"", "\"FR\""), out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_newer_list_is_downloaded_checked_and_stored()
    {
        var (refresher, feed, store) = Refresher();
        feed.Manifest = Manifest(ListJson);
        feed.Body = ListJson;

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Updated);
        store.List!.ListDate.Should().Be(new DateOnly(2026, 9, 15));

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task A_republished_list_of_the_same_date_is_downloaded_again()
    {
        var (refresher, feed, store) = Refresher();
        feed.Manifest = Manifest(ListJson);
        feed.Body = ListJson;
        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Updated);

        // A forced run of the workflow after a fix: same list date, other
        // content, so another hash.
        var fixedJson = ListJson.Replace("BAYER S.P.A.", "BAYER S.P.A. (fixed)");
        feed.Manifest = Manifest(fixedJson);
        feed.Body = fixedJson;

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Updated);
        store.List!.FindGroup("026622050")!.Members.Single(m => m.Code == "026622050").Holder
            .Should().Be("BAYER S.P.A. (fixed)");
        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(2);
    }

    [Fact]
    public async Task An_older_list_than_the_stored_one_is_not_downloaded()
    {
        var (refresher, feed, store) = Refresher();
        store.Save(Encoding.UTF8.GetBytes(ListJson));
        var older = ListJson.Replace("2026-09-15", "2026-08-15");
        feed.Manifest = Manifest(older, version: "20260815");
        feed.Body = older;

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(0);
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_manifest_is_rejected()
    {
        var (refresher, feed, store) = Refresher();
        feed.Manifest = Manifest(ListJson);
        feed.Body = ListJson.Replace("BAYER", "OTHER");

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Rejected);
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task A_list_whose_date_differs_from_the_manifest_is_rejected()
    {
        var (refresher, feed, store) = Refresher();
        var other = ListJson.Replace("2026-09-15", "2026-08-15");
        feed.Body = other;
        feed.Manifest = Manifest(other, version: "20260915");

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Rejected);
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task No_manifest_leaves_the_list_as_it_is()
    {
        var (refresher, feed, _) = Refresher();
        feed.Manifest = null;

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.ManifestUnavailable);
    }

    [Fact]
    public void Equivalents_are_refreshed_only_with_Italy_as_reference_country()
    {
        var options = new CatalogueFeedOptions();
        CatalogueFeedSelection.IncludesEquivalents("IT", options).Should().BeTrue();
        CatalogueFeedSelection.IncludesEquivalents("not a country", options).Should().BeTrue();
        CatalogueFeedSelection.IncludesEquivalents("FR", options).Should().BeFalse();
        options.EquivalentsEnabled = false;
        CatalogueFeedSelection.IncludesEquivalents("IT", options).Should().BeFalse();
        CatalogueFeedSelection.IncludesShortages("IT", options).Should().BeTrue("the two lists are switched separately");
        new CatalogueFeedOptions().ItalianFeedFolder(EquivalenceFeedDefinition.Instance.Folder)
            .Should().Be("https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/equivalents/");
    }

    [Theory]
    [InlineData("038253035", "https://codifa.it/farmaci/dettaglio/038253035")]
    [InlineData(" 038253035 ", "https://codifa.it/farmaci/dettaglio/038253035")]
    [InlineData("038253036", null)]
    [InlineData("38253035", null)]
    [InlineData("03825303A", null)]
    [InlineData("EU/1/96/007/001", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_Codifa_link_needs_a_valid_AIC(string? code, string? expected)
    {
        MedicineInfoLink.ForNationalCode(code)?.AbsoluteUri.Should().Be(expected);
        (MedicineInfoLink.ForNationalCode(code) is null).Should().Be(expected is null);
    }

    [Fact]
    public async Task The_query_lists_the_group_cheapest_first_with_shortages_and_home_stock()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryEquivalenceListStore { List = Parsed() };
        ShortageFeedParser.TryParseList(
            "{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[" +
            "{\"aic\":\"026622050\",\"start\":\"2026-08-01\",\"expectedEnd\":null,\"equivalent\":true,\"reason\":\"production\"}]}",
            out var shortages, out _).Should().BeTrue();
        scope.Shortages.List = shortages;
        var adalat = await AddAsync(scope, "Adalat", "026622050", stock: 10m);
        await AddAsync(scope, "Nifedipina EG", "037248022", stock: 14m);
        await AddAsync(scope, "Nifedipina DOC finished", "035831015", stock: 0m);
        await AddAsync(scope, "Unrelated", "035123037", stock: 28m);
        var query = new EquivalentsQuery(store, scope.Medicines, scope.Stock, scope.Clock, scope.Shortages);

        var view = await query.LoadAsync(" 026622050 ", adalat, default);

        view.State.Should().Be(EquivalentsState.Listed);
        view.ListDate.Should().Be(new DateOnly(2026, 9, 15));
        view.ShortageListDate.Should().Be(new DateOnly(2026, 9, 29));
        view.Group!.Code.Should().Be("12A");
        view.Rows.Select(r => r.Package.Name).Should().Equal("NIFEDIPINA DOC*", "NIFEDIPINA EG", "ADALAT CRONO");
        view.Current!.Package.Code.Should().Be("026622050");
        view.Current.Shortage.Should().NotBeNull("U2: the shortage list marks the package");
        view.Rows.Where(r => !r.IsCurrent).Should().OnlyContain(r => r.Shortage == null);
        view.Current.AtHome.Should().BeEmpty("the medicine itself is not its own equivalent");
        view.AtHome.Should().ContainSingle().Which.AtHome.Should().Equal("Nifedipina EG");
    }

    [Fact]
    public async Task Another_record_of_the_same_package_with_stock_is_at_home()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryEquivalenceListStore { List = Parsed() };
        var current = await AddAsync(scope, "Norvasc", "002783013", stock: 5m);
        await AddAsync(scope, "Norvasc (old therapy)", "002783013", stock: 20m);
        var query = new EquivalentsQuery(store, scope.Medicines, scope.Stock, scope.Clock);

        var view = await query.LoadAsync("002783013", current, default);

        view.Current!.AtHome.Should().Equal("Norvasc (old therapy)");
        view.AtHome.Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task The_query_joins_only_on_a_valid_AIC_and_tells_why_nothing_is_shown()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryEquivalenceListStore();
        var query = new EquivalentsQuery(store, scope.Medicines, scope.Stock, scope.Clock);

        (await query.LoadAsync("026622050", null, default)).State.Should().Be(EquivalentsState.NoList);
        store.List = Parsed();
        (await query.LoadAsync(null, null, default)).State.Should().Be(EquivalentsState.NoCode);
        (await query.LoadAsync("026622051", null, default)).State.Should().Be(EquivalentsState.NoCode, "wrong check digit");
        (await query.LoadAsync("EU/1/96/007/001", null, default)).State.Should().Be(EquivalentsState.NoCode);
        var notListed = await query.LoadAsync("038253035", null, default);
        notListed.State.Should().Be(EquivalentsState.NotListed);
        notListed.ListDate.Should().Be(new DateOnly(2026, 9, 15));
        query.IsListed("026622050").Should().BeTrue();
        query.IsListed("038253035").Should().BeFalse();
        query.IsListed(null).Should().BeFalse();
    }

    [Fact]
    public async Task The_shortage_tooltip_points_to_the_equivalents_when_the_package_is_listed()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        ShortageFeedParser.TryParseList(
            "{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[" +
            "{\"aic\":\"026622050\",\"start\":\"2026-08-01\",\"expectedEnd\":null,\"equivalent\":true,\"reason\":\"production\"}," +
            "{\"aic\":\"038253035\",\"start\":\"2026-08-01\",\"expectedEnd\":null,\"equivalent\":false,\"reason\":\"demand\"}]}",
            out var shortages, out _).Should().BeTrue();
        scope.Shortages.List = shortages;
        var listed = await AddAsync(scope, "Adalat", "026622050", stock: 10m);
        var other = await AddAsync(scope, "Gastroloc", "038253035", stock: 10m);
        var loc = new JsonDictionaryLocalizationService("en");
        var loader = new MedicineOverviewLoader(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions,
            scope.Slots, scope.Clock, loc, scope.Shortages,
            equivalents: new InMemoryEquivalenceListStore { List = Parsed() });

        var items = await loader.LoadAsync(default);

        var row = items.Single(i => i.Id == listed);
        row.NationalCode.Should().Be("026622050");
        row.SupplyDetail.Should().EndWith(loc.Get("Shortage.Detail.SeeEquivalents"));
        items.Single(i => i.Id == other).SupplyDetail.Should().NotContain(loc.Get("Shortage.Detail.SeeEquivalents"));
    }

    [Fact]
    public async Task The_shortage_tooltip_does_not_point_to_equivalents_for_a_code_failing_the_check_digit()
    {
        // The menu that opens the equivalents is disabled for such a code.
        const string invalid = "026622051";
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        ShortageFeedParser.TryParseList(
            "{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[" +
            "{\"aic\":\"" + invalid + "\",\"start\":\"2026-08-01\",\"expectedEnd\":null,\"equivalent\":true,\"reason\":\"production\"}]}",
            out var shortages, out _).Should().BeTrue();
        scope.Shortages.List = shortages;
        EquivalenceFeedParser.TryParseList(ListJson.Replace("026622050", invalid), out var list, out _).Should().BeTrue();
        var id = await AddAsync(scope, "Adalat", invalid, stock: 10m);
        var loc = new JsonDictionaryLocalizationService("en");
        var loader = new MedicineOverviewLoader(scope.Medicines, scope.Stock, scope.Schedules, scope.Suspensions,
            scope.Slots, scope.Clock, loc, scope.Shortages,
            equivalents: new InMemoryEquivalenceListStore { List = list });

        var row = (await loader.LoadAsync(default)).Single(i => i.Id == id);

        row.HasShortage.Should().BeTrue();
        row.SupplyDetail.Should().NotContain(loc.Get("Shortage.Detail.SeeEquivalents"));
    }

    [Theory]
    [InlineData(" 038253035 ", "038253035")]
    [InlineData("038253036", null)]
    [InlineData("EU/1/96/007/001", null)]
    [InlineData(null, null)]
    public void One_rule_says_which_codes_are_AICs(string? code, string? expected)
    {
        ItalianPharmacode.NormalizeAic(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_language_words_the_equivalents(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        foreach (var key in new[]
                 {
                     "Ui.MainForm.Menu.Therapy.Equivalents", "Ui.MainForm.Menu.Therapy.OpenCodifa",
                     "Ui.MedicineEditDialog.Documents.Codifa", "Ui.MedicineEditDialog.Documents.Equivalents",
                     "Ui.EquivalentsDialog.Disclaimer", "Ui.EquivalentsDialog.NoCode", "Ui.EquivalentsDialog.NoList",
                     "Ui.EquivalentsDialog.ThisMedicine", "Ui.EquivalentsDialog.Error.Load",
                     "Ui.EquivalentsDialog.Column.Medicine", "Ui.EquivalentsDialog.Column.Package",
                     "Ui.EquivalentsDialog.Column.Holder", "Ui.EquivalentsDialog.Column.Price",
                     "Ui.EquivalentsDialog.Column.Difference", "Ui.EquivalentsDialog.Column.AtHome",
                     "Ui.EquivalentsDialog.Column.Note", "Shortage.Detail.SeeEquivalents",
                 })
        {
            loc.Get(key);
        }
        loc.Get("Ui.EquivalentsDialog.Title", "Adalat");
        loc.Get("Ui.EquivalentsDialog.Group", "NIFEDIPINA", "14 UNITA'");
        loc.Get("Ui.EquivalentsDialog.ReferencePrice", "5.12 €");
        loc.Get("Ui.EquivalentsDialog.AtHome", "Adalat", "ADALAT CRONO");
        loc.Get("Ui.EquivalentsDialog.AtHome.Note", "Adalat", "ADALAT CRONO", "*note");
        loc.Get("Ui.EquivalentsDialog.Source", "15/09/2026");
        loc.Get("Ui.EquivalentsDialog.NotListed", "15/09/2026");

        loc.FallbackHits.Should().BeEmpty();
    }

    private static async Task<Guid> AddAsync(ApplicationTestScope scope, string name, string code, decimal stock)
    {
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows, EndDate: null,
            InitialQuantity: stock), default);
        var medicine = (await scope.Medicines.GetAsync(id, default))!;
        medicine.NationalCode = code;
        await scope.Medicines.UpdateAsync(medicine, default);
        return id;
    }

    private static (EquivalenceRefresher, FakeEquivalenceFeed, InMemoryEquivalenceListStore) Refresher()
    {
        var feed = new FakeEquivalenceFeed();
        var store = new InMemoryEquivalenceListStore();
        return (new EquivalenceRefresher(feed, store, NullLogger<EquivalenceRefresher>.Instance), feed, store);
    }

    private sealed class FakeEquivalenceFeed : IEquivalenceFeedClient
    {
        public string? Manifest { get; set; }
        public string Body { get; set; } = string.Empty;
        public int Downloads { get; private set; }

        public Task<DatedFeedManifest?> GetManifestAsync(CancellationToken cancellationToken)
            => Task.FromResult(Manifest is not null && EquivalenceFeedParser.TryParseManifest(Manifest, out var m, out _) ? m : null);

        public Task<byte[]> DownloadAsync(DatedFeedManifest manifest, CancellationToken cancellationToken)
        {
            Downloads++;
            return Task.FromResult(Encoding.UTF8.GetBytes(Body));
        }
    }
}
