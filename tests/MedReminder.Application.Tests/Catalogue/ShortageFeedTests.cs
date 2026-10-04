using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.Notifications;
using MedReminder.Application.Tests.Support;
using MedReminder.Application.UseCases;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

// Shortage feed, refresh and notices (docs/notes/EVOLUTION-PROPOSALS-2.md §3.3).
public class ShortageFeedTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private const string ListJson =
        "{\"country\":\"IT\",\"source\":\"AIFA\",\"listDate\":\"2026-09-29\",\"generated\":\"2026-10-01T00:00:00+00:00\"," +
        "\"entries\":[" +
        "{\"aic\":\"045348036\",\"start\":\"2025-07-28\",\"expectedEnd\":null,\"equivalent\":true,\"reason\":\"production\"}," +
        "{\"aic\":\"012345678\",\"start\":\"2028-05-01\",\"expectedEnd\":\"2028-12-31\",\"equivalent\":false,\"reason\":\"withdrawn\"}," +
        "{\"aic\":\"087654321\",\"start\":\"2026-01-01\",\"expectedEnd\":null,\"equivalent\":false,\"reason\":\"new-kind\"}]}";

    private static string Manifest(string json, string version = "20260929", long? size = null, string? sha = null)
        => $"{{\"country\":\"IT\",\"version\":\"{version}\",\"file\":\"shortages-{version}.json\"," +
           $"\"generated\":\"2026-10-01T00:00:00+00:00\",\"sha256\":\"{sha ?? Sha(json)}\"," +
           $"\"size\":{size ?? Encoding.UTF8.GetByteCount(json)},\"rows\":{{\"entries\":3}}}}";

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_valid_list_is_parsed()
    {
        ShortageFeedParser.TryParseList(ListJson, out var list, out var error).Should().BeTrue(error);

        list!.ListDate.Should().Be(new DateOnly(2026, 9, 29));
        list.Count.Should().Be(3);
        list.Find("045348036").Should().Be(
            new ShortageEntry("045348036", new DateOnly(2025, 7, 28), null, true, ShortageReason.Production));
        list.Find("012345678")!.ExpectedEnd.Should().Be(new DateOnly(2028, 12, 31));
        list.Find("087654321")!.Reason.Should().Be(ShortageReason.Other, "an unknown category reads as other");
    }

    [Theory]
    [InlineData("{\"country\":\"FR\",\"listDate\":\"2026-09-29\",\"entries\":[]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"29/09/2026\",\"entries\":[]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-29\"}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[{\"aic\":\"45348036\",\"start\":\"2025-07-28\"}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[{\"aic\":\"045348036\"}]}")]
    [InlineData("{\"country\":\"IT\",\"listDate\":\"2026-09-29\",\"entries\":[{\"aic\":\"045348036\",\"start\":\"2025-07-28\",\"expectedEnd\":\"x\"}]}")]
    [InlineData("not json")]
    public void An_invalid_list_is_rejected(string json)
    {
        ShortageFeedParser.TryParseList(json, out var list, out var error).Should().BeFalse();
        list.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_manifest_must_name_its_file_and_carry_a_hash_and_a_size()
    {
        ShortageFeedParser.TryParseManifest(Manifest(ListJson), out var manifest, out _).Should().BeTrue();
        manifest!.ListDate.Should().Be(new DateOnly(2026, 9, 29));

        ShortageFeedParser.TryParseManifest(Manifest(ListJson).Replace("shortages-20260929", "other-20260929"), out _, out _)
            .Should().BeFalse();
        ShortageFeedParser.TryParseManifest(Manifest(ListJson, version: "202609"), out _, out _).Should().BeFalse();
        ShortageFeedParser.TryParseManifest(Manifest(ListJson, sha: "abc"), out _, out _).Should().BeFalse();
        ShortageFeedParser.TryParseManifest(Manifest(ListJson, size: 0), out _, out _).Should().BeFalse();
        ShortageFeedParser.TryParseManifest(Manifest(ListJson).Replace("\"IT\"", "\"FR\""), out _, out _).Should().BeFalse();
    }

    // The file the shortage workflow published in this repository parses.
    [Fact]
    public void The_published_feed_parses()
    {
        var folder = Path.Combine(RepoRoot(), "data", "it", "shortages");
        var manifestJson = File.ReadAllText(Path.Combine(folder, "latest.json"));
        ShortageFeedParser.TryParseManifest(manifestJson, out var manifest, out var error).Should().BeTrue(error);
        var bytes = File.ReadAllBytes(Path.Combine(folder, manifest!.File));

        bytes.LongLength.Should().Be(manifest.Size);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(manifest.Sha256);
        ShortageFeedParser.TryParseList(Encoding.UTF8.GetString(bytes), out var list, out error).Should().BeTrue(error);
        list!.ListDate.Should().Be(manifest.ListDate);
        list.Count.Should().BeGreaterThan(300);
    }

    [Fact]
    public async Task A_newer_list_is_downloaded_checked_and_stored()
    {
        var (refresher, feed, store) = Refresher();
        feed.Manifest = Manifest(ListJson);
        feed.Body = ListJson;

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Updated);
        store.List!.ListDate.Should().Be(new DateOnly(2026, 9, 29));

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.UpToDate);
        feed.Downloads.Should().Be(1);
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_manifest_is_rejected()
    {
        var (refresher, feed, store) = Refresher();
        feed.Manifest = Manifest(ListJson);
        feed.Body = ListJson.Replace("production", "demand");

        (await refresher.RunAsync(default)).Should().Be(DatedListRefreshOutcome.Rejected);
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task A_list_whose_date_differs_from_the_manifest_is_rejected()
    {
        var (refresher, feed, store) = Refresher();
        var other = ListJson.Replace("2026-09-29", "2026-09-22");
        feed.Body = other;
        feed.Manifest = Manifest(other, version: "20260929");

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
    public void Shortages_are_refreshed_only_with_Italy_as_reference_country()
    {
        var options = new CatalogueFeedOptions();
        CatalogueFeedSelection.IncludesShortages("IT", options).Should().BeTrue();
        CatalogueFeedSelection.IncludesShortages("not a country", options).Should().BeTrue();
        CatalogueFeedSelection.IncludesShortages("FR", options).Should().BeFalse();
        options.ShortagesEnabled = false;
        CatalogueFeedSelection.IncludesShortages("IT", options).Should().BeFalse();
        new CatalogueFeedOptions().ItalianFeedFolder(ShortageFeedDefinition.Instance.Folder)
            .Should().Be("https://raw.githubusercontent.com/vger70/MedReminder/feeds/data/it/shortages/");
    }

    [Fact]
    public async Task A_listed_medicine_is_notified_once_per_start_date()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        ShortageFeedParser.TryParseList(ListJson, out var list, out _);
        scope.Shortages.List = list;
        var listed = await AddAsync(scope, "Lamivudina", "045348036");
        var expected = await AddAsync(scope, "Futuro", "012345678");
        _ = await AddAsync(scope, "Other", "111111111");
        _ = await AddAsync(scope, "No code", null);

        (await scope.ShortageNotices.RunAsync(Today, sendsEmail: true, default)).Should().Be(2);
        (await scope.ShortageNotices.RunAsync(Today, sendsEmail: true, default)).Should().Be(0);

        scope.Windows.Sent.Select(s => s.Title).Should().BeEquivalentTo("Lamivudina: in shortage", "Futuro: shortage announced");
        scope.Windows.Targets.Should().BeEquivalentTo(new[] { NotificationTarget.Shortage(listed), NotificationTarget.Shortage(expected) });
        scope.Email.Sent.Should().HaveCount(2);
        scope.Email.Sent[0].Body.Should().Contain("Ask your doctor or pharmacist");
    }

    [Fact]
    public async Task Email_goes_only_from_a_device_that_sends_email_and_the_periodic_check_runs_the_notices()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        ShortageFeedParser.TryParseList(ListJson, out var list, out _);
        scope.Shortages.List = list;
        await AddAsync(scope, "Lamivudina", "045348036");
        scope.Master.SendsEmail = false;

        await scope.Monitor.RunAsync(default);

        scope.ShortageNoticeEvents.All.Should().ContainSingle();
        scope.Email.Sent.Should().BeEmpty();
        scope.Windows.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task The_medicine_list_shows_the_shortage_and_its_detail()
    {
        var scope = new ApplicationTestScope(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        ShortageFeedParser.TryParseList(ListJson, out var list, out _);
        scope.Shortages.List = list;
        var listed = await AddAsync(scope, "Lamivudina", "045348036");
        var other = await AddAsync(scope, "Other", "111111111");
        var loc = new JsonDictionaryLocalizationService("en");
        var loader = new MedReminder.Application.Overview.MedicineOverviewLoader(scope.Medicines, scope.Stock,
            scope.Schedules, scope.Suspensions, scope.Slots, scope.Clock, loc, scope.Shortages);
        string D(int y, int m, int d) => new DateOnly(y, m, d).ToString("d", loc.CurrentCulture);

        var items = await loader.LoadAsync(default);

        var row = items.Single(i => i.Id == listed);
        row.SupplyDisplay.Should().Be("In shortage");
        row.SupplyDetail.Should().Contain("since " + D(2025, 7, 28)).And.Contain("equivalent medicines")
            .And.Contain("Ask your doctor or pharmacist").And.Contain(D(2026, 9, 29));
        items.Single(i => i.Id == other).HasShortage.Should().BeFalse();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("it")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Every_language_words_every_shortage(string language)
    {
        var loc = new JsonDictionaryLocalizationService(language);
        foreach (var reason in Enum.GetValues<ShortageReason>())
        {
            foreach (var state in Enum.GetValues<ShortageState>())
            {
                var notice = new ShortageNotice(new ShortageEntry("045348036", Today, Today, true, reason), state);
                ShortageTexts.Display(notice, loc);
                ShortageTexts.Detail(notice, Today, loc);
                ShortageTexts.Notification("Enalapril", notice, loc);
            }
        }
        var noEnd = new ShortageNotice(new ShortageEntry("045348036", Today, null, false, ShortageReason.Other),
            ShortageState.Current);
        ShortageTexts.Detail(noEnd, Today, loc);
        loc.Get("Ui.MainForm.Column.Supply");

        loc.FallbackHits.Should().BeEmpty();
    }

    private static async Task<Guid> AddAsync(ApplicationTestScope scope, string name, string? code)
    {
        var id = await scope.AddMedicine.ExecuteAsync(new AddMedicineCommand(
            Name: name, Unit: "tablets", DosePerAdministration: 1m, AdministrationsPerDay: 1,
            StartDate: Today, ThresholdDays: 7,
            NotificationChannels: NotificationChannels.Windows | NotificationChannels.Email, EndDate: null,
            InitialQuantity: 100m), default);
        var medicine = (await scope.Medicines.GetAsync(id, default))!;
        medicine.NationalCode = code;
        await scope.Medicines.UpdateAsync(medicine, default);
        return id;
    }

    private static (ShortageRefresher, FakeShortageFeed, InMemoryShortageListStore) Refresher()
    {
        var feed = new FakeShortageFeed();
        var store = new InMemoryShortageListStore();
        return (new ShortageRefresher(feed, store, NullLogger<ShortageRefresher>.Instance), feed, store);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedReminder.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeShortageFeed : IShortageFeedClient
    {
        public string? Manifest { get; set; }
        public string Body { get; set; } = string.Empty;
        public int Downloads { get; private set; }

        public Task<DatedFeedManifest?> GetManifestAsync(CancellationToken cancellationToken)
            => Task.FromResult(Manifest is not null && ShortageFeedParser.TryParseManifest(Manifest, out var m, out _) ? m : null);

        public Task<byte[]> DownloadAsync(DatedFeedManifest manifest, CancellationToken cancellationToken)
        {
            Downloads++;
            return Task.FromResult(Encoding.UTF8.GetBytes(Body));
        }
    }
}
