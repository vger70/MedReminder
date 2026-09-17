using MedReminder.DataImporter.Database;
using Microsoft.Extensions.Logging;

namespace MedReminder.DataImporter.Import;

public sealed class AifaImporter(AifaCsvLoader loader, ImportRepository repository, ILogger<AifaImporter> logger)
{
    public async Task<ImportStatistics> RunAsync(AifaImportFiles files, bool updateCore, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var metadata = await Task.WhenAll(
            loader.GetMetadataAsync(files.PackagesPath, cancellationToken),
            loader.GetMetadataAsync(files.IngredientsPath, cancellationToken),
            loader.GetMetadataAsync(files.AtcPath, cancellationToken));
        var runId = await repository.CreateRunAsync(metadata, cancellationToken);
        long packages = 0, ingredients = 0, atc = 0, rejected = 0;
        try
        {
            logger.LogInformation("AIFA import run {ImportRunId} started.", runId);
            packages = await repository.LoadPackagesAsync(runId, loader, files.PackagesPath, value => ReportProgress("packages", value), cancellationToken);
            ingredients = await repository.LoadIngredientsAsync(runId, loader, files.IngredientsPath, value => ReportProgress("ingredients", value), cancellationToken);
            atc = await repository.LoadAtcAsync(runId, loader, files.AtcPath, value => ReportProgress("ATC", value), cancellationToken);

            logger.LogInformation("Validating staged AIFA data.");
            var validation = await repository.ValidateAsync(runId, cancellationToken);
            rejected = validation.RejectedRows;
            var statistics = new ImportStatistics(packages, ingredients, atc, rejected, DateTimeOffset.UtcNow - started);
            if (!validation.IsValid)
            {
                await repository.CompleteRunAsync(runId, statistics, "VALIDATION_FAILED", string.Join(" ", validation.Errors), cancellationToken);
                throw new ValidationException(validation.Errors);
            }

            if (updateCore)
            {
                logger.LogInformation("Normalizing validated AIFA data into core.");
                await repository.NormalizeAsync(runId, cancellationToken);
                await repository.CompleteRunAsync(runId, statistics, "COMPLETED", null, cancellationToken);
            }
            else
            {
                await repository.CompleteRunAsync(runId, statistics, "COMPLETED", "Validation-only run; core was not modified.", cancellationToken);
            }
            return statistics;
        }
        catch (OperationCanceledException)
        {
            var statistics = new ImportStatistics(packages, ingredients, atc, rejected, DateTimeOffset.UtcNow - started);
            await TryMarkFailedAsync(runId, statistics, "Import cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            var statistics = new ImportStatistics(packages, ingredients, atc, rejected, DateTimeOffset.UtcNow - started);
            await TryMarkFailedAsync(runId, statistics, exception.Message);
            throw;
        }
    }

    private void ReportProgress(string dataset, long rowNumber)
    {
        if (rowNumber % 10_000 == 0) logger.LogInformation("Loading {Dataset}: {Rows} rows.", dataset, rowNumber);
    }

    private async Task TryMarkFailedAsync(long runId, ImportStatistics statistics, string error)
    {
        try { await repository.CompleteRunAsync(runId, statistics, "FAILED", error, CancellationToken.None); }
        catch (Exception markException) { logger.LogError(markException, "Could not mark AIFA import run {ImportRunId} as failed.", runId); }
    }
}
