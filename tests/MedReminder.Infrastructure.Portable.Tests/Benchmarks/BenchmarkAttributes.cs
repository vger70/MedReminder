using Xunit;

namespace MedReminder.Infrastructure.Tests.Benchmarks;

// Opt-in query benchmarks (issue #328). They seed large SQLite profiles
// and take minutes, so a plain `dotnet test` (CI included) skips them.
// Run them with:
//
//   MEDREMINDER_BENCHMARKS=1 dotnet test tests/MedReminder.Infrastructure.Portable.Tests \
//     -c Release --filter "Category=Benchmark" --logger "console;verbosity=detailed"
//
// MEDREMINDER_BENCHMARK_RUNS sets the measured runs per operation
// (default 7); MEDREMINDER_BENCHMARK_OUT appends the result tables to a
// Markdown file.
internal static class BenchmarkSwitch
{
    public const string Variable = "MEDREMINDER_BENCHMARKS";

    public static string? SkipReason
        => Environment.GetEnvironmentVariable(Variable) == "1"
            ? null
            : $"Opt-in benchmark: set {Variable}=1 to run it.";

    public static int Runs
        => int.TryParse(Environment.GetEnvironmentVariable("MEDREMINDER_BENCHMARK_RUNS"), out var runs) && runs > 0
            ? runs
            : 7;

    public static string? OutputFile => Environment.GetEnvironmentVariable("MEDREMINDER_BENCHMARK_OUT");
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class BenchmarkTheoryAttribute : TheoryAttribute
{
    public BenchmarkTheoryAttribute()
    {
        Skip = BenchmarkSwitch.SkipReason;
    }
}
