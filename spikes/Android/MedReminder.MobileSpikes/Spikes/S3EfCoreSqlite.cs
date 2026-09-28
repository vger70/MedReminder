using System.Data;
using System.Globalization;
using System.Text.Json;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Export;
using MedReminder.Application.Overview;
using MedReminder.Domain.Medicines;
using MedReminder.Infrastructure;
using MedReminder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedReminder.MobileSpikes.Spikes;

// S3 — EF Core SQLite on Android with Release trimming / AOT (P13,
// §13 Phase 0). Runs the production registrations
// (AddMedReminderPortableInfrastructure), DatabaseInitializer on a new
// and on an existing database, a write through the repositories and the
// unit of work, the MedicineOverviewLoader read, and the
// reflection-based System.Text.Json path that ArchiveReader and the sync
// codec use. Synthetic data only, in a scratch folder of the app.
internal static class S3EfCoreSqlite
{
    private const string Spike = "S3";
    private const string SyntheticName = "S3 synthetic medicine";

    public static async Task RunAsync(SpikeReport report, string directory, CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "medreminder.db");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILocalizationService, KeyEchoLocalization>();
        services.AddMedReminderPortableInfrastructure(databasePath);
        services.AddScoped<MedicineOverviewLoader>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        await SpikeRunner.CheckAsync(report, Spike, "DatabaseInitializer on a new database", async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<MedReminderDbContext>();
            var tables = await ScalarAsync(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table';", cancellationToken);
            var medicines = await ScalarAsync(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'Medicines';", cancellationToken);
            return (medicines == "1", $"{tables} tables; Medicines table present: {medicines == "1"}");
        });

        await SpikeRunner.CheckAsync(report, Spike, "Write through IMedicineRepository and IUnitOfWork", async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var now = DateTimeOffset.Now;
            var medicine = new Medicine
            {
                Name = SyntheticName,
                Unit = "tablets",
                DosePerAdministration = 1.5m,
                AdministrationsPerDay = 2,
                StartDate = DateOnly.FromDateTime(now.Date),
                ThresholdDays = 7,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await scope.ServiceProvider.GetRequiredService<IMedicineRepository>().AddAsync(medicine, cancellationToken);
            var rows = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            return (rows >= 1, $"{rows} row(s) written");
        });

        await SpikeRunner.CheckAsync(report, Spike, "Read through the repositories (ListActiveAsync)", async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var active = await scope.ServiceProvider.GetRequiredService<IMedicineRepository>().ListActiveAsync(cancellationToken);
            var found = active.SingleOrDefault(m => m.Name == SyntheticName);
            var ok = found is { DosePerAdministration: 1.5m, AdministrationsPerDay: 2 };
            return (ok, ok ? "decimal and DateOnly columns read back" : $"{active.Count} active medicine(s); values not read back as written");
        });

        await SpikeRunner.CheckAsync(report, Spike, "MedicineOverviewLoader.LoadAsync", async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var items = await scope.ServiceProvider.GetRequiredService<MedicineOverviewLoader>().LoadAsync(cancellationToken);
            var ok = items.Count == 1 && items[0].Name == SyntheticName;
            return (ok, $"{items.Count} item(s)");
        });

        await SpikeRunner.CheckAsync(report, Spike, "DatabaseInitializer on the existing database (boot patches)", async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<MedReminderDbContext>();
            var journal = await ScalarAsync(db, "PRAGMA journal_mode;", cancellationToken);
            var version = await ScalarAsync(db, "SELECT sqlite_version();", cancellationToken);
            var ok = string.Equals(journal, "wal", StringComparison.OrdinalIgnoreCase);
            return (ok, $"journal_mode {journal}; SQLite {version}");
        });

        SpikeRunner.Check(report, Spike, "Reflection-based System.Text.Json (ArchiveReader, sync codec path)", () =>
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(Argon2Params.Default, options);
            var back = JsonSerializer.Deserialize<Argon2Params>(json, options);
            var ok = back == Argon2Params.Default;
            return (ok, ok ? $"round trip ok: {json}" : "round trip returned a different value");
        });

        SqliteConnection.ClearAllPools();
    }

    private static async Task<string> ScalarAsync(MedReminderDbContext db, string sql, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // MedicineOverviewLoader needs a localization service; the production
    // one reads user.settings.json through IAppDataLocation, which the
    // spike does not have.
    private sealed class KeyEchoLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public string Get(string key, params object?[] args) => key;

        public string GetIn(string languageCode, string key, params object?[] args) => key;
    }
}
