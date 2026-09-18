using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using MedReminder.Infrastructure.Catalogue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedReminder.UI.Hosting;

// Boot-time reference-catalogue import
// (ANALYSIS-DRUG-CATALOGUE.md §2.6, M2 §3.3 B).
//
// Runs once on startup, in the background, if the feature flag is on:
//   - opens the embedded AIFA snapshot from
//     MedReminder.Infrastructure.Assets.Catalogue.it.aifa-*.zip
//     via EmbeddedSnapshotProvider,
//   - hands it to CsvReferenceCatalogueImporter which short-circuits
//     when the recorded snapshot_version already matches (idempotent
//     replay = zero work).
//
// Nothing blocks the UI: the whole run lives on a background thread
// pool task started from ExecuteAsync. When the flag is off the
// service is not registered at all (see Program.BuildHost).
internal sealed class CatalogueRefreshHostedService : BackgroundService
{
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

        var italy = CountryCode.Parse("IT");
        if (!provider.TryOpen(italy, out var stream, out var snapshotVersion))
        {
            _log.LogInformation(
                "No embedded reference-catalogue snapshot for {Country}; skipping boot import.",
                italy.Value);
            return;
        }

        await using (stream)
        {
            _log.LogInformation(
                "Starting reference-catalogue import for {Country}, snapshot {Version}.",
                italy.Value, snapshotVersion);

            var report = await importer.ImportAsync(stream, italy, snapshotVersion, cancellationToken);

            _log.LogInformation(
                "Reference-catalogue import for {Country} complete: inserted={Inserted}, deleted={Deleted}, skipped={Skipped}, version={Version}, completedAt={CompletedAt}.",
                italy.Value,
                report.Inserted, report.Deleted, report.Skipped,
                report.SnapshotVersion, report.CompletedAt);
        }
    }
}
