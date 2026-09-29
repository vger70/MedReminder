using MedReminder.DataImporter.Configuration;
using MedReminder.DataImporter.Database;
using MedReminder.DataImporter.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MedReminder.DataImporter;

internal static class Program
{
    private const int Success = 0;
    private const int ImportFailure = 1;
    private const int ValidationFailure = 2;
    private const int InvalidCommand = 3;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") { PrintHelp(); return Success; }
        if (args[0] is "--version" or "-v") { Console.WriteLine(typeof(Program).Assembly.GetName().Version); return Success; }
        if (args[0] is not ("import-aifa" or "validate-aifa" or "export-sqlite")) { Console.Error.WriteLine("Unknown command."); PrintHelp(); return InvalidCommand; }

        try
        {
            if (args[0] == "export-sqlite") return await ExportSqliteAsync(args.Skip(1).ToArray());
            var files = ParseFiles(args.Skip(1).ToArray(), out var connectionOverride);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            using var host = BuildHost(connectionOverride);
            var importer = host.Services.GetRequiredService<AifaImporter>();
            var result = await importer.RunAsync(files, args[0] == "import-aifa", cancellation.Token);
            Console.WriteLine();
            Console.WriteLine(args[0] == "import-aifa" ? "AIFA import completed successfully." : "AIFA validation completed successfully; core was not modified.");
            Console.WriteLine($"Packages:    {result.Packages}");
            Console.WriteLine($"Ingredients: {result.Ingredients}");
            Console.WriteLine($"ATC entries: {result.Atc}");
            Console.WriteLine($"Duration:    {result.Duration:hh\\:mm\\:ss}");
            return Success;
        }
        catch (ValidationException exception) { Console.Error.WriteLine($"Validation failed: {exception.Message}"); return ValidationFailure; }
        catch (OperationCanceledException) { Console.Error.WriteLine("Import cancelled."); return ImportFailure; }
        catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return InvalidCommand; }
        catch (Exception exception) { Log.Error(exception, "AIFA importer failed."); Console.Error.WriteLine($"Import failed: {exception.Message}"); return ImportFailure; }
        finally { await Log.CloseAndFlushAsync(); }
    }

    private static IHost BuildHost(string? connectionOverride)
    {
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddJsonFile("appsettings.Development.json", optional: true).AddEnvironmentVariables();
        builder.Logging.ClearProviders(); builder.Logging.AddSerilog();
        var options = builder.Configuration.GetSection(ImporterOptions.SectionName).Get<ImporterOptions>() ?? new ImporterOptions();
        var connectionString = connectionOverride ?? builder.Configuration.GetConnectionString("MedReminder");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A PostgreSQL connection string is required. Use --connection-string or ConnectionStrings__MedReminder.");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new ImportRepository(connectionString, options));
        builder.Services.AddSingleton(provider => new SqliteExporter(
            connectionString,
            options,
            provider.GetRequiredService<ILogger<SqliteExporter>>()));
        builder.Services.AddSingleton<AifaCsvLoader>();
        builder.Services.AddSingleton<AifaImporter>();
        return builder.Build();
    }

    private static async Task<int> ExportSqliteAsync(string[] args)
    {
        string? output = null;
        string? connectionString = null;
        var overwrite = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--overwrite": overwrite = true; break;
                case "--output" when ++index < args.Length: output = args[index]; break;
                case "--connection-string" when ++index < args.Length: connectionString = args[index]; break;
                default: throw new ArgumentException($"Unknown option or missing value: '{args[index]}'.");
            }
        }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        using var host = BuildHost(connectionString);
        await host.Services.GetRequiredService<SqliteExporter>().ExportAsync(output ?? string.Empty, overwrite, cancellation.Token);
        Console.WriteLine($"SQLite pharmaceutical catalog created: {Path.GetFullPath(output!)}");
        return Success;
    }

    private static AifaImportFiles ParseFiles(string[] args, out string? connectionString)
    {
        string? packages = null, ingredients = null, atc = null; connectionString = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (++index >= args.Length) throw new ArgumentException($"Missing value for '{args[index - 1]}'.");
            switch (args[index - 1])
            {
                case "--packages": packages = args[index]; break;
                case "--ingredients": ingredients = args[index]; break;
                case "--atc": atc = args[index]; break;
                case "--connection-string": connectionString = args[index]; break;
                default: throw new ArgumentException($"Unknown option '{args[index - 1]}'.");
            }
        }
        if (packages is null || ingredients is null || atc is null) throw new ArgumentException("--packages, --ingredients and --atc are required.");
        return new AifaImportFiles(packages, ingredients, atc);
    }

    private static void PrintHelp() => Console.WriteLine("""
        MedReminder.DataImporter

        Commands:
          import-aifa   Load, validate and normalize AIFA CSV files into PostgreSQL.
          validate-aifa Load and validate AIFA CSV files without modifying core.
          export-sqlite Export the PostgreSQL pharmaceutical catalog to a separate SQLite file.

        Required options:
          --packages <path>      confezioni_fornitura.csv
          --ingredients <path>   PA_confezioni.csv
          --atc <path>           atc.csv

        Optional:
          --connection-string <connection string>
          export-sqlite --output <path> [--overwrite] [--connection-string <connection string>]
          --help, -h
          --version, -v
        """);
}
