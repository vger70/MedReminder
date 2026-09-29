using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedReminder.UI.Tests.Hosting;

// Remote-feed step of the boot import: a failing feed never stops the
// next one, and the configuration binds onto the per-feed defaults.
public sealed class CatalogueRefreshHostedServiceTests
{
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
    public void Configured_feeds_merge_into_the_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Catalogue:RemoteFeed:Enabled"] = "true",
                ["Catalogue:RemoteFeed:Feeds:ES:Enabled"] = "true",
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
        options.Feeds.Should().HaveCount(4);
    }
}
