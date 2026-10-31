using System.Diagnostics;
using System.Globalization;
using System.Text;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Coverage;
using MedReminder.Application.Ledger;
using MedReminder.Application.Monitoring;
using MedReminder.Application.Notifications;
using MedReminder.Application.Overview;
using MedReminder.Application.Timeline;
using MedReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MedReminder.Infrastructure.Tests.Benchmarks;

// Query benchmarks on seeded profiles (issue #328), opt-in: see
// BenchmarkSwitch. Each operation runs once to warm up, then
// BenchmarkSwitch.Runs times, each run in a fresh scope (a fresh
// DbContext, so nothing is served from the change tracker). The table
// gives the median, minimum and maximum in milliseconds, and the SQLite
// query plan of the commands the operation ran.
//
// Repository rows sum one call per medicine; loader rows are the
// loader end to end.
[Trait("Category", "Benchmark")]
public sealed class QueryBenchmarks(ITestOutputHelper output)
{
    public static TheoryData<int, int> Profiles => new() { { 5, 1 }, { 15, 2 }, { 30, 5 } };

    // The sync apply checks a sample of facts one by one, as
    // ApplyRemoteOperations does for each incoming operation.
    private const int SyncSample = 50;

    [BenchmarkTheory]
    [MemberData(nameof(Profiles))]
    public async Task Hot_reads(int medicines, int years)
    {
        await using var profile = await BenchmarkProfile.CreateAsync(medicines, years);
        var results = new List<(string Name, double Median, double Min, double Max, IReadOnlyList<string> Plan)>();

        foreach (var (name, operation) in Operations(profile))
        {
            results.Add(await MeasureAsync(profile, name, operation));
        }

        Report(profile, results);
    }

    private static IEnumerable<(string Name, Func<IServiceProvider, Task> Operation)> Operations(BenchmarkProfile p)
    {
        var ct = CancellationToken.None;
        var ids = p.MedicineIds;
        var sample = p.IntakeIds.Where((_, i) => i % Math.Max(1, p.IntakeIds.Count / SyncSample) == 0)
            .Take(SyncSample).ToList();
        var medicineOf = new Dictionary<Guid, Guid>();

        Func<IServiceProvider, Task> PerMedicine<T>(Func<T, Guid, Task> read) where T : notnull
            => async sp =>
            {
                var repository = sp.GetRequiredService<T>();
                foreach (var id in ids) await read(repository, id);
            };

        // Repository calls.
        yield return ("intakes: ListForMedicineAsync (history)",
            PerMedicine<IMedicationIntakeRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("intakes: ListForMedicineBetweenAsync (90 days)",
            PerMedicine<IMedicationIntakeRepository>((r, id) => r.ListForMedicineBetweenAsync(id, p.Today.AddDays(-89), p.Today, ct)));
        yield return ("intakes: ListForDayAsync (today)",
            PerMedicine<IMedicationIntakeRepository>((r, id) => r.ListForDayAsync(id, p.Today, ct)));
        yield return ("intakes: ListMedicinesWithDayIntakeAsync (once)",
            sp => sp.GetRequiredService<IMedicationIntakeRepository>().ListMedicinesWithDayIntakeAsync(p.Today, ct));
        yield return ("stock: ListForMedicineAsync (history)",
            PerMedicine<IStockMovementRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("stock: GetCurrentStockAsync",
            PerMedicine<IStockMovementRepository>((r, id) => r.GetCurrentStockAsync(id, ct)));
        yield return ("stock: GetLastNewPackageQuantityAsync",
            PerMedicine<IStockMovementRepository>((r, id) => r.GetLastNewPackageQuantityAsync(id, ct)));
        yield return ("stock: GetLastConsumptionDayAsync",
            PerMedicine<IStockMovementRepository>((r, id) => r.GetLastConsumptionDayAsync(id, ct)));
        yield return ("counts: ListForMedicineAsync (history)",
            PerMedicine<IStockCountRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("retractions: ListForMedicineAsync",
            PerMedicine<IFactRetractionRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("operations: ListForMedicineAsync",
            PerMedicine<ISyncOperationRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("operations: ExistsForMedicineAsync (MedicineDeleted)",
            PerMedicine<ISyncOperationRepository>((r, id) => r.ExistsForMedicineAsync(id, "MedicineDeleted", ct)));
        yield return ("operations: ListStampsForMedicineAsync",
            PerMedicine<ISyncOperationRepository>((r, id) => r.ListStampsForMedicineAsync(id, ct)));
        yield return ("prescriptions: ListForMedicineAsync",
            PerMedicine<IPrescriptionRepository>((r, id) => r.ListForMedicineAsync(id, ct)));
        yield return ("packages: ListForMedicineAsync",
            PerMedicine<IStockPackageRepository>((r, id) => r.ListForMedicineAsync(id, ct)));

        // The fact look-ups of the sync apply, one incoming intake at a
        // time: is it retracted, is it already stored.
        yield return ($"sync apply: {SyncSample} intakes looked up in the history", async sp =>
        {
            var retractions = sp.GetRequiredService<IFactRetractionRepository>();
            var intakes = sp.GetRequiredService<IMedicationIntakeRepository>();
            var db = sp.GetRequiredService<MedReminderDbContext>();
            if (medicineOf.Count == 0)
            {
                foreach (var row in await db.MedicationIntakes.AsNoTracking()
                             .Where(i => sample.Contains(i.Id)).Select(i => new { i.Id, i.MedicineId }).ToListAsync())
                {
                    medicineOf[row.Id] = row.MedicineId;
                }
            }
            foreach (var id in sample)
            {
                var medicineId = medicineOf[id];
                _ = (await retractions.ListForMedicineAsync(medicineId, ct)).All(r => r.FactId != id);
                _ = (await intakes.ListForMedicineAsync(medicineId, ct)).Any(i => i.Id == id);
            }
        });

        yield return ($"sync apply: {SyncSample} intakes looked up by id", async sp =>
        {
            var retractions = sp.GetRequiredService<IFactRetractionRepository>();
            var intakes = sp.GetRequiredService<IMedicationIntakeRepository>();
            foreach (var id in sample)
            {
                var medicineId = medicineOf[id];
                _ = await retractions.GetForFactAsync(medicineId, id, ct) is null;
                _ = await intakes.GetAsync(medicineId, id, ct) is not null;
            }
        });

        // Write cost of the intake indexes: 1,000 intakes, rolled back.
        yield return ("write: 1000 intakes (rolled back)", async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            for (var i = 0; i < 1000; i++)
            {
                db.MedicationIntakes.Add(new Domain.Medicines.MedicationIntake
                {
                    MedicineId = ids[i % ids.Count], Day = p.Today.AddDays(1 + i / ids.Count), Quantity = 1m,
                    Status = Domain.Medicines.IntakeStatus.Taken,
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.RollbackAsync(ct);
        });

        // Write cost of the movement indexes: 1,000 derived consumptions,
        // rolled back.
        yield return ("write: 1000 movements (rolled back)", async sp =>
        {
            var db = sp.GetRequiredService<MedReminderDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            for (var i = 0; i < 1000; i++)
            {
                db.StockMovements.Add(new Domain.Stock.StockMovement
                {
                    MedicineId = ids[i % ids.Count],
                    OccurredAt = new DateTimeOffset(p.Today.AddDays(1 + i / ids.Count).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
                    Kind = Domain.Stock.StockMovementKind.Consumption, QuantityDelta = -2m, StockEpoch = 1,
                    Origin = Domain.Stock.StockMovementOrigin.Derived,
                });
            }
            await db.SaveChangesAsync(ct);
            await transaction.RollbackAsync(ct);
        });

        // Startup: the boot patches run on every launch of an existing
        // database.
        yield return ("boot: DatabaseInitializer.InitializeAsync",
            sp => sp.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct));

        // Hot loaders, end to end.
        yield return ("loader: NotificationPlanLoader.LoadAsync",
            sp => sp.GetRequiredService<NotificationPlanLoader>().LoadAsync(p.Today, ct));
        yield return ("loader: NotificationPlanLoader.LoadDeadlinesAsync",
            sp => sp.GetRequiredService<NotificationPlanLoader>().LoadDeadlinesAsync(ct));
        yield return ("loader: MedicationMonitor.RunAsync",
            sp => sp.GetRequiredService<MedicationMonitor>().RunAsync(ct));
        yield return ("loader: DoseReminderService.RunAsync",
            sp => sp.GetRequiredService<DoseReminderService>().RunAsync(ct));
        yield return ("loader: CoveragePlanQuery.LoadAsync (30 days)",
            sp => sp.GetRequiredService<CoveragePlanQuery>().LoadAsync(p.Today, p.Today.AddDays(30), ct));
        yield return ("loader: TherapyTimelineQuery.LoadAsync",
            sp => sp.GetRequiredService<TherapyTimelineQuery>().LoadAsync(null, ct));
        yield return ("loader: MedicineOverviewLoader.LoadAsync",
            sp => ActivatorUtilities.CreateInstance<MedicineOverviewLoader>(sp).LoadAsync(ct));
        yield return ("loader: LedgerFactsLoader.LoadAsync (every medicine)", async sp =>
        {
            var loader = sp.GetRequiredService<LedgerFactsLoader>();
            foreach (var medicine in await sp.GetRequiredService<IMedicineRepository>().ListAllAsync(ct))
            {
                await loader.LoadAsync(medicine, ct);
            }
        });
    }

    private static async Task<(string, double, double, double, IReadOnlyList<string>)> MeasureAsync(
        BenchmarkProfile profile, string name, Func<IServiceProvider, Task> operation)
    {
        // Warm-up, recording the commands for the query plan.
        profile.Recorder.Clear();
        profile.Recorder.Armed = true;
        await using (var scope = profile.CreateScope())
        {
            await operation(scope.ServiceProvider);
        }
        profile.Recorder.Armed = false;
        IReadOnlyList<string> plan;
        await using (var scope = profile.CreateScope())
        {
            plan = await profile.Recorder.PlansAsync(
                scope.ServiceProvider.GetRequiredService<MedReminderDbContext>().Database.GetDbConnection());
        }

        var times = new List<double>();
        for (var run = 0; run < BenchmarkSwitch.Runs; run++)
        {
            await using var scope = profile.CreateScope();
            var watch = Stopwatch.StartNew();
            await operation(scope.ServiceProvider);
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (name, times[times.Count / 2], times[0], times[^1], plan);
    }

    private void Report(
        BenchmarkProfile profile,
        IReadOnlyList<(string Name, double Median, double Min, double Max, IReadOnlyList<string> Plan)> results)
    {
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(inv, $"### {profile.Medicines} medicines, {profile.Years} years");
        text.AppendLine();
        text.AppendLine(inv, $"Rows: {string.Join(", ", profile.RowCounts.Select(c => $"{c.Key} {c.Value}"))}. " +
            $"Database: {profile.SizeBytes / 1024.0 / 1024.0:F1} MB. Runs: {BenchmarkSwitch.Runs}. Machine: {Environment.OSVersion}, {Environment.ProcessorCount} cores, " +
            $".NET {Environment.Version}.");
        text.AppendLine();
        text.AppendLine("| Operation | Median ms | Min | Max | Query plan |");
        text.AppendLine("|---|---:|---:|---:|---|");
        foreach (var r in results)
        {
            text.AppendLine(inv,
                $"| {r.Name} | {r.Median:F1} | {r.Min:F1} | {r.Max:F1} | {string.Join("; ", r.Plan.Select(Shorten))} |");
        }
        text.AppendLine();

        output.WriteLine(text.ToString());
        if (BenchmarkSwitch.OutputFile is { } file)
        {
            File.AppendAllText(file, text.ToString());
        }
    }

    // "SEARCH MedicationIntakes USING INDEX IX_... (MedicineId=?)" is kept;
    // the plan of every distinct command is listed once.
    private static string Shorten(string detail) => detail.Replace("|", "/", StringComparison.Ordinal);
}
