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

// Boot-time reference-catalogue import
// (ANALYSIS-DRUG-CATALOGUE.md §2.6, M2 §3.3 B, M3 §3.4).
//
// Runs once on startup, in the background, if the feature flag is on.
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
// After the embedded snapshots, the Italian catalogue is refreshed
// from the remote AIFA feed (ANALYSIS-CATALOGUE-REMOTE-FEED.md §4.1):
// the step waits for MainForm's startup update check (at most
// RemoteFeedSignalTimeout), then runs RemoteCatalogueRefresher when
// both Catalogue:RemoteFeed:Enabled and the user's "check for updates
// at startup" setting are on. Running it on the same task as the
// embedded imports keeps every catalogue write sequential.
//
// Nothing blocks the UI: the whole run lives on a background thread
// pool task started from ExecuteAsync. When the flag is off the
// service is not registered at all (see Program.BuildHost).
internal sealed class CatalogueRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan RemoteFeedSignalTimeout = TimeSpan.FromSeconds(60);

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
    private readonly ILogger<CatalogueRefreshHostedService> _log;

    public CatalogueRefreshHostedService(
        IServiceProvider services,
        ILogger<CatalogueRefreshHostedService> log)
    {
        _services = services;
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
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Clean shutdown before the import got a chance to
                // start or complete.
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Reference-catalogue boot import failed.");
            }
        }, stoppingToken);
        return Task.CompletedTask;
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CatalogueFeatureOptions>>();
        if (!options.CurrentValue.Enabled)
        {
            return;
        }

        var provider = scope.ServiceProvider.GetRequiredService<EmbeddedSnapshotProvider>();
        var importer = scope.ServiceProvider.GetRequiredService<IReferenceCatalogueImporter>();

        foreach (var country in ImportOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportCountryAsync(provider, importer, country, cancellationToken);
        }

        await RefreshFromRemoteFeedAsync(cancellationToken);
    }

    private async Task RefreshFromRemoteFeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var signal = _services.GetRequiredService<StartupUpdateCheckSignal>();
            await signal.WaitAsync(RemoteFeedSignalTimeout, cancellationToken);

            await using var scope = _services.CreateAsyncScope();

            var feedOptions = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CatalogueFeedOptions>>().CurrentValue;
            if (!feedOptions.Enabled)
            {
                _log.LogInformation("Remote AIFA feed disabled by configuration; skipping.");
                return;
            }

            var userSettings = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<UserSettings>>().CurrentValue;
            if (!userSettings.CheckForUpdatesOnStartup)
            {
                _log.LogInformation("Remote AIFA feed skipped: checking for updates at startup is off.");
                return;
            }

            var refresher = scope.ServiceProvider.GetRequiredService<RemoteCatalogueRefresher>();
            await refresher.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The embedded imports above are already committed; a
            // failure here only means the catalogue stays as it is.
            _log.LogWarning(ex, "Remote AIFA feed refresh failed; catalogue unchanged.");
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
