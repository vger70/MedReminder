using System.Data.Common;
using MedReminder.Application;
using MedReminder.Application.Abstractions;
using MedReminder.Application.Notifications;
using MedReminder.Domain.Ledger;
using MedReminder.Domain.Medicines;
using MedReminder.Domain.Notifications;
using MedReminder.Domain.Prescriptions;
using MedReminder.Domain.Stock;
using MedReminder.Domain.Sync;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Persistence;
using MedReminder.Infrastructure.Profiles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MedReminder.Infrastructure.Tests.Benchmarks;

// A seeded profile database behind the portable host the mobile app
// composes (issue #328). Every medicine has two doses a day over the
// whole period, with what a long-lived profile accumulates:
//
//   - two intakes a day (with their sync operations);
//   - one derived consumption movement a day and a new package every
//     fifteen days (with its package row);
//   - one stock count and one prescription a month;
//   - a schedule change a year, a few retractions and suspensions.
//
// The stock stays well above the threshold, so the monitor reads every
// medicine and sends nothing.
internal sealed class BenchmarkProfile : IAsyncDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public int Medicines { get; }
    public int Years { get; }
    public DateOnly Today { get; }
    public IReadOnlyList<Guid> MedicineIds { get; private set; } = [];
    public IReadOnlyList<Guid> IntakeIds { get; private set; } = [];
    public QueryPlanRecorder Recorder { get; } = new();
    public IReadOnlyDictionary<string, long> RowCounts { get; private set; } = new Dictionary<string, long>();
    public long SizeBytes { get; private set; }

    private BenchmarkProfile(int medicines, int years)
    {
        Medicines = medicines;
        Years = years;
        Today = DateOnly.FromDateTime(DateTime.Now);
        _root = Path.Combine(Path.GetTempPath(), "mr-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var profilesRoot = Path.Combine(_root, "profiles");
        var registry = new ProfileRegistry(Path.Combine(_root, "profiles.json"), profilesRoot, TimeProvider.System);
        var current = new CurrentProfile(registry.Create("Bench", ProfileRole.Admin), profilesRoot);

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IAppDataLocation>(new Location(_root));
        services.AddSingleton<IProfileRegistry>(registry);
        services.AddSingleton<ICurrentProfile>(current);
        services.Configure<SmtpSettings>(_ => { });
        services.Configure<BackupSettings>(_ => { });
        services.Configure<UserSettings>(_ => { });
        services.AddMedReminderApplication();
        services.AddMedReminderPortableInfrastructure(current.DatabasePath);
        services.AddMedReminderPortableProfileSettings(profilesRoot);
        services.AddSingleton<IEmailNotificationService, SilentNotifications>();
        services.AddSingleton<IWindowsNotificationService, SilentNotifications>();
        services.ConfigureDbContext<MedReminderDbContext>(o => o.AddInterceptors(Recorder));
        _provider = services.BuildServiceProvider();
    }

    public static async Task<BenchmarkProfile> CreateAsync(int medicines, int years)
    {
        var profile = new BenchmarkProfile(medicines, years);
        await using (var scope = profile.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        }
        await profile.SeedAsync();
        return profile;
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    private async Task SeedAsync()
    {
        var start = Today.AddYears(-Years);
        var at = (DateOnly day, int hour) => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
        var device = Guid.NewGuid();
        var medicineIds = new List<Guid>();
        var intakeIds = new List<Guid>();
        long hlc = at(start, 0).ToUnixTimeMilliseconds();

        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedReminderDbContext>();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await using var transaction = await db.Database.BeginTransactionAsync();

        for (var n = 0; n < Medicines; n++)
        {
            var medicine = new Medicine
            {
                Name = $"Medicine {n:D2}",
                Unit = "tablets",
                DosePerAdministration = 1m,
                AdministrationsPerDay = 2,
                StartDate = start,
                ThresholdDays = 7,
                NotificationChannels = NotificationChannels.Windows,
                CreatedAt = at(start, 7),
                UpdatedAt = at(start, 7),
            };
            db.Medicines.Add(medicine);
            medicineIds.Add(medicine.Id);

            for (var year = 0; year < Years; year++)
            {
                db.MedicationScheduleHistories.Add(new MedicationScheduleHistory
                {
                    MedicineId = medicine.Id,
                    EffectiveFrom = start.AddYears(year),
                    DosePerAdministration = 1m,
                    AdministrationsPerDay = 2,
                    RecordedAt = at(start.AddYears(year), 7),
                });
            }

            var set = new MedicationAdministrationSlotSet
                { MedicineId = medicine.Id, EffectiveFrom = start, RecordedAt = at(start, 7) };
            db.MedicationAdministrationSlotSets.Add(set);
            for (var order = 0; order < 2; order++)
            {
                db.MedicationAdministrationSlots.Add(new MedicationAdministrationSlot
                {
                    MedicineId = medicine.Id, SetId = set.Id, Dose = 1m,
                    Time = new TimeOnly(order == 0 ? 8 : 20, 0), Order = order,
                });
            }

            db.StockMovements.Add(new StockMovement
            {
                MedicineId = medicine.Id, OccurredAt = at(start, 7), Kind = StockMovementKind.InitialLoad,
                QuantityDelta = 200m, StockEpoch = 1, Origin = StockMovementOrigin.User,
            });

            var retracted = 0;
            for (var day = start; day <= Today; day = day.AddDays(1))
            {
                var index = day.DayNumber - start.DayNumber;
                foreach (var hour in (int[])[8, 20])
                {
                    var intake = new MedicationIntake
                    {
                        MedicineId = medicine.Id, Day = day, Quantity = 1m, Status = IntakeStatus.Taken,
                        ScheduledAt = at(day, hour), SlotTime = new TimeOnly(hour, 0),
                        ActualAt = at(day, hour), RecordedAt = at(day, hour),
                    };
                    db.MedicationIntakes.Add(intake);
                    intakeIds.Add(intake.Id);
                    db.SyncOperations.Add(new SyncOperation
                    {
                        HlcPhysicalMs = hlc++, HlcCounter = 0, DeviceId = device, Generation = 1,
                        Type = "IntakeRecorded", SchemaVersion = 1, MedicineId = medicine.Id,
                        EntityId = intake.Id, Payload = new string('x', 240),
                    });
                    if (index % 97 == 0 && hour == 8 && retracted++ < 10)
                    {
                        db.FactRetractions.Add(new FactRetraction
                        {
                            MedicineId = medicine.Id, FactId = Guid.NewGuid(), Kind = FactKind.Intake,
                            RecordedAt = at(day, 21),
                        });
                    }
                }

                db.StockMovements.Add(new StockMovement
                {
                    MedicineId = medicine.Id, OccurredAt = at(day, 12), Kind = StockMovementKind.Consumption,
                    QuantityDelta = -2m, StockEpoch = 1, Origin = StockMovementOrigin.Derived,
                });

                if (index % 15 == 0)
                {
                    var entry = new StockMovement
                    {
                        MedicineId = medicine.Id, OccurredAt = at(day, 9), Kind = StockMovementKind.NewPackage,
                        QuantityDelta = 30m, StockEpoch = 1, Origin = StockMovementOrigin.User,
                    };
                    db.StockMovements.Add(entry);
                    db.StockPackages.Add(new StockPackage
                    {
                        MedicineId = medicine.Id, MovementId = entry.Id, Quantity = 30m,
                        ExpiresOn = day.AddYears(2), OpenedOn = day,
                        ClosedOn = day.AddDays(15) <= Today ? day.AddDays(15) : null,
                        Closure = day.AddDays(15) <= Today ? PackageClosure.Finished : null,
                        RecordedAt = at(day, 9), UpdatedAt = at(day, 9),
                    });
                }

                if (day.Day == 1)
                {
                    db.StockCounts.Add(new StockCount
                    {
                        MedicineId = medicine.Id, CountDay = day, CountedQuantity = 200m, TakenToday = 0m,
                        ThresholdAtCount = 7, RecordedAt = at(day, 22),
                    });
                    db.Prescriptions.Add(new Prescription
                    {
                        MedicineId = medicine.Id, IssuedOn = day, Packages = 2, ValidUntil = day.AddDays(30),
                        CollectedOn = day.AddDays(2), RecordedAt = at(day, 10), UpdatedAt = at(day, 10),
                    });
                }

                if (index % 180 == 90)
                {
                    db.MedicationSuspensions.Add(new MedicationSuspension
                    {
                        MedicineId = medicine.Id, StartDate = day, EndDate = day.AddDays(3),
                        RecordedAt = at(day, 10),
                    });
                }
            }

            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync();
        MedicineIds = medicineIds;
        IntakeIds = intakeIds;

        var counts = new Dictionary<string, long>();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        foreach (var table in (string[])["MedicationIntakes", "StockMovements", "StockCounts", "SyncOperations",
                     "Prescriptions", "StockPackages", "FactRetractions"])
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size();";
            SizeBytes = (long)(await command.ExecuteScalarAsync())!;
        }
        RowCounts = counts;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class Location(string directory) : IAppDataLocation
    {
        public string DataDirectory => directory;
    }

    private sealed class SilentNotifications : IEmailNotificationService, IWindowsNotificationService
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task ShowAsync(string title, string body, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

// Records the commands EF runs while armed, so the benchmark can ask
// SQLite for their query plan. Test code only: nothing is logged.
internal sealed class QueryPlanRecorder : DbCommandInterceptor
{
    private readonly List<(string Sql, (string Name, object? Value)[] Parameters)> _commands = [];

    public bool Armed { get; set; }

    public IReadOnlyList<(string Sql, (string Name, object? Value)[] Parameters)> Commands => _commands;

    public void Clear() => _commands.Clear();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        if (!Armed) return;
        _commands.Add((command.CommandText,
            [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, (object?)p.Value))]));
    }

    // The plan rows (`detail` column) of every recorded command, distinct.
    public async Task<IReadOnlyList<string>> PlansAsync(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        var plans = new List<string>();
        foreach (var (sql, parameters) in _commands.DistinctBy(c => c.Sql))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plans.Add(reader.GetString(reader.GetOrdinal("detail")));
            }
        }
        return [.. plans.Distinct()];
    }
}
