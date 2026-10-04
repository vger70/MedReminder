using MedReminder.Application.Abstractions;
using MedReminder.Application.Catalogue;
using MedReminder.Application.UpdateChecking;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Hosting;

// Reference-catalogue import at boot and daily remote-feed check
// (ANALYSIS-DRUG-CATALOGUE.md §2.6, M2 §3.3 B, M3 §3.4,
// ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.1).
//
// The embedded snapshots are imported once on startup, in the
// background, if the feature flag is on.
// For each supported country a snapshot is embedded for, opens it via
// EmbeddedSnapshotProvider and hands it to CsvReferenceCatalogueImporter,
// which short-circuits when the recorded snapshot_version already
// matches (idempotent replay = zero work).
//
// Each country is imported in its own transaction (owned by the
// importer). A failure on one country is logged and swallowed so the
// next country still runs — a broken EU snapshot must never take the
// Italian catalogue offline, and vice versa.
//
// After the embedded snapshots, the catalogues the user reads (the
// reference country and EU, CatalogueFeedSelection) are refreshed from
// their remote feeds (ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.1,
// ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §5): the step waits for
// MainForm's startup update check (at most RemoteFeedSignalTimeout),
// then runs RemoteCatalogueRefresher once per feed when both
// Catalogue:RemoteFeed:Enabled and the user's "check for app and
// catalogue updates" setting (UserSettings.CheckForUpdatesOnStartup)
// are on.
//
// The remote step then repeats during the session: a tick every
// TickInterval runs it again once RemoteCheckInterval has passed since
// the last run. An app left open for days (autostart, tray, sleep
// instead of shutdown) would otherwise never see a feed published after
// its start. The hourly tick, compared with the wall clock, catches up
// right after a resume from sleep. The last run is kept in memory only:
// every start checks anyway. The gates are read again on every tick, so
// turning the setting on, or changing the reference country, takes
// effect without a restart. Running every step on the same task keeps
// every catalogue write sequential.
//
// Nothing blocks the UI: the whole run lives on a background thread
// pool task started from ExecuteAsync. When the flag is off the
// service is not registered at all (see Program.BuildHost).
internal sealed class CatalogueRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan RemoteFeedSignalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan RemoteCheckInterval = TimeSpan.FromHours(24);

    // The ticks come from a monotonic timer, the times compared from the
    // wall clock: a tick can read a few ms short of 24 h after the one
    // that ran the step. Half a tick of slack keeps the check on the
    // 24th tick instead of drifting to the 25th.
    private static readonly TimeSpan DueSlack = TickInterval / 2;

    // Ordered so IT runs first (default reference country), then the
    // supranational EU catalogue, then the M4 national additions
    // (ES → AEMPS, FR → BDPM). Each country runs in its own
    // transaction; a broken snapshot for one never stops the next.
    private static readonly IReadOnlyList<CountryCode> ImportOrder = new[]
    {
        CountryCode.Parse("IT"),
        CountryCode.Parse("EU"),
        CountryCode.Parse("ES"),
        CountryCode.Parse("FR"),
    };

    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly ILogger<CatalogueRefreshHostedService> _log;

    public CatalogueRefreshHostedService(
        IServiceProvider services,
        TimeProvider clock,
        ILogger<CatalogueRefreshHostedService> log)
    {
        _services = services;
        _clock = clock;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fire-and-forget so the host's start-up sequence is not
        // blocked by the import. Any failure is logged and swallowed
        // — the app must remain usable even if the snapshot cannot be
        // read.
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown, at any point of the boot import or of
                // the daily loop.
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Reference-catalogue refresh stopped; the next start retries.");
            }
        }, stoppingToken);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await ImportEmbeddedSnapshotsAsync(cancellationToken))
        {
            return;
        }

        var startedAt = _clock.GetUtcNow();
        DateTimeOffset? lastRemoteRun =
            await RefreshFromRemoteFeedAsync(atStartup: true, cancellationToken) ? startedAt : null;

        using var timer = new PeriodicTimer(TickInterval, _clock);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = _clock.GetUtcNow();
            if (!IsRemoteCheckDue(lastRemoteRun, now))
            {
                continue;
            }

            if (await RefreshFromRemoteFeedAsync(atStartup: false, cancellationToken))
            {
                lastRemoteRun = now;
            }
        }
    }

    // Due when the remote step never ran in this session (a gate was
    // off), when RemoteCheckInterval has passed (less DueSlack), or when
    // the clock went back before the last run (a manual clock change
    // would otherwise hold the check back by the same amount).
    internal static bool IsRemoteCheckDue(DateTimeOffset? lastRun, DateTimeOffset now) =>
        lastRun is not { } last || now < last || now - last >= RemoteCheckInterval - DueSlack;

    // Returns false when the catalogue feature is off. The scope, and
    // with it the SQLite connection the importer opened, is disposed
    // before the remote step starts: backup restore, archive import and
    // sync join move the database file and fail on Windows while
    // another handle is open (CLAUDE.md §7).
    private async Task<bool> ImportEmbeddedSnapshotsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CatalogueFeatureOptions>>();
        if (!options.CurrentValue.Enabled)
        {
            return false;
        }

        var provider = scope.ServiceProvider.GetRequiredService<EmbeddedSnapshotProvider>();
        var importer = scope.ServiceProvider.GetRequiredService<IReferenceCatalogueImporter>();

        foreach (var country in ImportOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportCountryAsync(provider, importer, country, cancellationToken);
        }

        return true;
    }

    // Returns false when a gate kept the step from running, so the next
    // tick checks again; true once it ran or failed to start (a failure
    // waits for the next interval rather than repeating every hour).
    // Skips are logged at Information at startup only, so a gate left
    // off does not add a line to the log every hour.
    private async Task<bool> RefreshFromRemoteFeedAsync(bool atStartup, CancellationToken cancellationToken)
    {
        var skipLevel = atStartup ? LogLevel.Information : LogLevel.Debug;
        IReadOnlyList<CatalogueFeedDescriptor> feeds;
        bool shortages;
        bool equivalents;
        try
        {
            // Gates first, so a disabled step neither waits for the
            // update-check signal nor holds anything while it waits.
            var feedOptions = _services.GetRequiredService<IOptionsMonitor<CatalogueFeedOptions>>().CurrentValue;
            if (!feedOptions.Enabled)
            {
                _log.Log(skipLevel, "Remote catalogue feeds disabled by configuration; skipping.");
                return false;
            }

            var userSettings = _services.GetRequiredService<IOptionsMonitor<UserSettings>>().CurrentValue;
            if (!userSettings.CheckForUpdatesOnStartup)
            {
                _log.Log(skipLevel, "Remote catalogue feeds skipped: checking for app and catalogue updates is off.");
                return false;
            }

            feeds = CatalogueFeedSelection.Select(userSettings.ReferenceCountry, feedOptions);
            shortages = CatalogueFeedSelection.IncludesShortages(userSettings.ReferenceCountry, feedOptions);
            equivalents = CatalogueFeedSelection.IncludesEquivalents(userSettings.ReferenceCountry, feedOptions);
            if (feeds.Count == 0)
            {
                _log.Log(skipLevel, "No remote catalogue feed enabled for the reference country; skipping.");
                return false;
            }

            if (atStartup)
            {
                var signal = _services.GetRequiredService<StartupUpdateCheckSignal>();
                await signal.WaitAsync(RemoteFeedSignalTimeout, cancellationToken);
            }
            else
            {
                _log.LogInformation("Daily remote catalogue check started.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Remote catalogue feeds could not start; catalogues unchanged.");
            return true;
        }

        await RefreshFeedsAsync(feeds, RefreshFeedAsync, _log, cancellationToken);
        if (shortages) await RefreshShortagesAsync(cancellationToken);
        if (equivalents) await RefreshEquivalentsAsync(cancellationToken);
        return true;
    }

    // The shortage list (EVOLUTION-PROPOSALS-2 §3.3) after the catalogues.
    // A file outside the profile database: no scope holds the database
    // longer than the refresher needs.
    private async Task RefreshShortagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ShortageRefresher>().RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shortage list refresh failed; list unchanged.");
        }
    }

    // The equivalents list (ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK §2.4)
    // after the shortage list, under the same rules.
    private async Task RefreshEquivalentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EquivalenceRefresher>().RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Equivalents list refresh failed; list unchanged.");
        }
    }

    // Own scope per feed, opened only now and disposed as soon as that
    // feed ends, for the same reason as the embedded step above.
    private async Task RefreshFeedAsync(CatalogueFeedDescriptor feed, CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var refresher = scope.ServiceProvider.GetRequiredService<RemoteCatalogueRefresher>();
        await refresher.RunAsync(feed, cancellationToken);
    }

    // Runs the feeds in order. A failure in one is logged and never
    // stops the next: the embedded imports are already committed, so a
    // failure only means that catalogue stays as it is.
    internal static async Task RefreshFeedsAsync(
        IReadOnlyList<CatalogueFeedDescriptor> feeds,
        Func<CatalogueFeedDescriptor, CancellationToken, Task> refresh,
        ILogger log,
        CancellationToken cancellationToken)
    {
        foreach (var feed in feeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await refresh(feed, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    ex, "Remote catalogue feed {Country} refresh failed; catalogue unchanged, other feeds continue.",
                    feed.Country.Value);
            }
        }
    }

    private async Task ImportCountryAsync(
        EmbeddedSnapshotProvider provider,
        IReferenceCatalogueImporter importer,
        CountryCode country,
        CancellationToken cancellationToken)
    {
        if (!provider.TryOpen(country, out var stream, out var snapshotVersion))
        {
            _log.LogInformation(
                "No embedded reference-catalogue snapshot for {Country}; skipping boot import.",
                country.Value);
            return;
        }

        await using (stream)
        {
            _log.LogInformation(
                "Starting reference-catalogue import for {Country}, snapshot {Version}.",
                country.Value, snapshotVersion);

            try
            {
                var report = await importer.ImportAsync(stream, country, snapshotVersion, cancellationToken);

                _log.LogInformation(
                    "Reference-catalogue import for {Country} complete: inserted={Inserted}, deleted={Deleted}, skipped={Skipped}, version={Version}, completedAt={CompletedAt}.",
                    country.Value,
                    report.Inserted, report.Deleted, report.Skipped,
                    report.SnapshotVersion, report.CompletedAt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Per-country isolation: a broken snapshot for one
                // country must not stop the import of the next one.
                // The importer runs each country in its own
                // transaction, so a rollback here does not touch rows
                // written for a previously-completed country.
                _log.LogError(ex,
                    "Reference-catalogue import for {Country} failed; other countries continue.",
                    country.Value);
            }
        }
    }
}
