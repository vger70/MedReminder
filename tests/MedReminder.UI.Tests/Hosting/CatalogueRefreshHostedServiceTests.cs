using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.UI.Tests.Hosting;

// Remote-feed step of the boot import and of the daily check: a failing
// feed never stops the next one, the daily check waits for its interval,
// and the configuration binds onto the per-feed defaults.
public sealed class CatalogueRefreshHostedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_failing_feed_does_not_stop_the_next_one()
    {
        var feeds = new[] { CatalogueFeedDescriptor.EuropeanUnion, CatalogueFeedDescriptor.Spain };
        var ran = new List<string>();

        await CatalogueRefreshHostedService.RefreshFeedsAsync(
            feeds,
            (feed, _) =>
            {
                ran.Add(feed.Country.Value);
                return feed == CatalogueFeedDescriptor.EuropeanUnion
                    ? Task.FromException(new InvalidOperationException("scope failed"))
                    : Task.CompletedTask;
            },
            NullLogger.Instance,
            CancellationToken.None);

        ran.Should().Equal("EU", "ES");
    }

    [Fact]
    public async Task Cancellation_stops_the_loop()
    {
        using var cts = new CancellationTokenSource();
        var ran = new List<string>();

        var act = () => CatalogueRefreshHostedService.RefreshFeedsAsync(
            [CatalogueFeedDescriptor.Italy, CatalogueFeedDescriptor.EuropeanUnion],
            (feed, ct) =>
            {
                ran.Add(feed.Country.Value);
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            NullLogger.Instance,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ran.Should().Equal("IT");
    }

    [Fact]
    public void Remote_check_is_due_when_it_never_ran_in_the_session()
    {
        CatalogueRefreshHostedService.IsRemoteCheckDue(null, Now).Should().BeTrue();
    }

    [Fact]
    public void Remote_check_waits_for_the_interval()
    {
        var last = Now - CatalogueRefreshHostedService.RemoteCheckInterval;

        CatalogueRefreshHostedService.IsRemoteCheckDue(last + TimeSpan.FromHours(1), Now).Should().BeFalse(
            "the 23rd hourly tick is too early");
        CatalogueRefreshHostedService.IsRemoteCheckDue(last + TimeSpan.FromMilliseconds(5), Now).Should().BeTrue(
            "the 24th tick runs even when it reads a few ms short of 24 h");
        CatalogueRefreshHostedService.IsRemoteCheckDue(last, Now).Should().BeTrue();
        CatalogueRefreshHostedService.IsRemoteCheckDue(last - TimeSpan.FromDays(3), Now).Should().BeTrue(
            "a resume after days of sleep catches up at the first tick");
    }

    [Fact]
    public void Remote_check_is_due_when_the_clock_went_back()
    {
        CatalogueRefreshHostedService.IsRemoteCheckDue(Now + TimeSpan.FromHours(2), Now).Should().BeTrue();
    }

    [Fact]
    public void Configured_feeds_merge_into_the_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalogue:RemoteFeed:Enabled"] = "true",
                ["Catalogue:RemoteFeed:Feeds:EU:Enabled"] = "false",
                ["Catalogue:RemoteFeed:Feeds:fr:MaxDownloadBytes"] = "1024",
            })
            .Build();

        var options = new CatalogueFeedOptions();
        configuration.GetSection(CatalogueFeedOptions.SectionName).Bind(options);

        options.IsFeedEnabled(CatalogueFeedDescriptor.Italy).Should().BeTrue();
        options.IsFeedEnabled(CatalogueFeedDescriptor.Spain).Should().BeTrue();
        options.IsFeedEnabled(CatalogueFeedDescriptor.EuropeanUnion).Should().BeFalse();
        options.MaxDownloadBytesFor(CatalogueFeedDescriptor.Italy).Should().Be(64L * 1024 * 1024);
        options.MaxDownloadBytesFor(CatalogueFeedDescriptor.Spain).Should().Be(16L * 1024 * 1024);
        options.MaxDownloadBytesFor(CatalogueFeedDescriptor.France).Should().Be(1024);
        options.IsFeedEnabled(CatalogueFeedDescriptor.UnitedStates).Should().BeTrue();
        options.MaxDownloadBytesFor(CatalogueFeedDescriptor.UnitedStates).Should().Be(64L * 1024 * 1024);
        options.Feeds.Should().HaveCount(5);
    }

    [Theory]
    [InlineData("IT", "US", true)]
    [InlineData("US", "IT", true)]
    [InlineData("IT", "EU", true)]
    [InlineData("US", "US", false)]
    [InlineData("us", "US", false)]
    [InlineData(null, "IT", false)]
    [InlineData("", "IT", false)]
    [InlineData("Italia", "IT", false)]
    [InlineData(null, "US", true)]
    public void Reference_country_change_compares_catalogue_countries(string? refreshed, string? current, bool changed)
    {
        CatalogueRefreshHostedService.ReferenceCountryChanged(refreshed, current).Should().Be(changed);
    }
}
