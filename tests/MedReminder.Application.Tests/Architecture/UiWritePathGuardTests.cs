using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Architecture;

// B.1 Phase 2a guard (ANALYSIS-B1-MOBILE-SYNC.md §2 P8, §7.2): every
// profile-data write goes through an Application use case, so that the
// sync layer can later emit one operation per use case. This test scans
// the UI sources and fails when one of them calls a repository write
// method, IUnitOfWork.SaveChangesAsync or the EF Core context directly.
//
// Why a source scan: the UI project targets net10.0-windows and cannot
// be loaded on Linux CI, and the check must not add a NuGet dependency
// (an architecture-rules library or Roslyn). A text scan is cheap and
// runs everywhere. It cannot see an entity obtained from a repository
// and mutated in place, but such a mutation only persists through a
// repository write or SaveChangesAsync, which the scan does catch.
//
// The write-method names are read by reflection from the repository
// ports in MedReminder.Application.Abstractions, so a new write method
// on a port is covered without editing this file.
public class UiWritePathGuardTests
{
    // Relative path (forward slashes, from the repository root) → reason
    // the direct write must stay in the UI. Keep it empty; add an entry
    // only with a reason reviewers can check.
    private static readonly IReadOnlyDictionary<string, string> AllowList =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // Read-side prefixes on repository ports. Any other public method
    // is treated as a write.
    private static readonly string[] ReadPrefixes = ["Get", "List", "Exists", "Count", "Find"];

    private static readonly string[] EfCoreTokens =
    [
        "MedReminderDbContext",
        "DbContext",
        "DbSet<",
        "ExecuteSqlRaw",
        "ExecuteSqlInterpolated",
        "ExecuteUpdate",
        "ExecuteDelete",
    ];

    [Fact]
    public void Write_method_list_covers_the_known_ports()
    {
        var names = WriteMethodNames();

        names.Should().Contain(["AddAsync", "AddRangeAsync", "UpdateAsync",
            "AddSetAsync", "PruneOlderThanAsync", "SaveChangesAsync", "AppendAsync"]);
        names.Should().NotContain(["GetAsync", "ListAllAsync", "ExistsAsync"]);
    }

    [Fact]
    public void Scanner_detects_a_direct_write()
    {
        var pattern = BuildPattern(WriteMethodNames());

        FindViolations("await repo.UpdateAsync(medicine, ct);", pattern).Should().NotBeEmpty();
        FindViolations("await uow.SaveChangesAsync(ct);", pattern).Should().NotBeEmpty();
        FindViolations("var db = sp.GetRequiredService<MedReminderDbContext>();", pattern).Should().NotBeEmpty();
        FindViolations("await usecase.ExecuteAsync(cmd, ct);", pattern).Should().BeEmpty();
        FindViolations("// repo.UpdateAsync(x) is gone", pattern).Should().BeEmpty();
    }

    [Fact]
    public void Ui_sources_do_not_write_profile_data_directly()
    {
        var root = FindRepositoryRoot();
        var uiDir = Path.Combine(root, "src", "MedReminder.UI");
        var files = Directory.EnumerateFiles(uiDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(uiDir, f))
            .ToList();

        // Guard against a vacuous pass after a move or rename.
        files.Should().Contain(f => f.EndsWith(Path.Combine("Forms", "MainForm.cs"), StringComparison.Ordinal));

        var pattern = BuildPattern(WriteMethodNames());
        var violations = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (AllowList.ContainsKey(relative)) continue;

            foreach (var (line, text) in FindViolations(File.ReadAllText(file), pattern))
            {
                violations.Add($"{relative}:{line}: {text}");
            }
        }

        violations.Should().BeEmpty(
            "UI code must call an Application use case instead of writing through repositories, " +
            "IUnitOfWork or the DbContext (ANALYSIS-B1-MOBILE-SYNC.md §7.2)");
    }

    // P8: the replicated profile settings (display name, notification
    // recipients) are written by RenameProfile and
    // UpdateNotificationSettings, never by the UI. Program.cs only adds
    // notifications.settings.json to the configuration it reads.
    [Fact]
    public void Ui_sources_do_not_write_replicated_profile_settings()
    {
        var root = FindRepositoryRoot();
        var uiDir = Path.Combine(root, "src", "MedReminder.UI");
        var pattern = new Regex(@"\.\s*Rename\s*\(|\bNotificationSettingsPath\b", RegexOptions.CultureInvariant);
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(uiDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !IsBuildOutput(uiDir, f)))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == "src/MedReminder.UI/Program.cs") continue;
            foreach (var (line, text) in FindViolations(File.ReadAllText(file), pattern))
            {
                violations.Add($"{relative}:{line}: {text}");
            }
        }

        violations.Should().BeEmpty(
            "the profile name and the notification recipients are replicated settings (ANALYSIS-B1-MOBILE-SYNC.md P8)");
    }

    [Fact]
    public void Allow_list_entries_point_to_existing_files()
    {
        var root = FindRepositoryRoot();
        foreach (var (path, reason) in AllowList)
        {
            File.Exists(Path.Combine(root, path)).Should().BeTrue($"allow-listed file {path} must exist");
            reason.Should().NotBeNullOrWhiteSpace($"allow-listed file {path} needs a reason");
        }
    }

    private static IReadOnlyList<string> WriteMethodNames()
    {
        var ports = typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(t => t.IsInterface
                && t.Namespace == typeof(IUnitOfWork).Namespace
                && (t.Name.EndsWith("Repository", StringComparison.Ordinal)
                    || t == typeof(IUnitOfWork) || t == typeof(IOperationLog)));

        return ports
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.Name)
            .Where(n => !ReadPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private static Regex BuildPattern(IReadOnlyList<string> writeMethods)
    {
        var calls = string.Join("|", writeMethods.Select(Regex.Escape));
        var ef = string.Join("|", EfCoreTokens.Select(Regex.Escape));
        return new Regex($@"\.\s*({calls})\s*\(|\b({ef})", RegexOptions.CultureInvariant);
    }

    private static IEnumerable<(int Line, string Text)> FindViolations(string source, Regex pattern)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var code = StripLineComment(lines[i]).Trim();
            if (code.Length == 0 || code.StartsWith('*') || code.StartsWith("/*", StringComparison.Ordinal))
                continue;
            if (pattern.IsMatch(code))
                yield return (i + 1, code);
        }
    }

    // Drops a trailing "//" comment. A "//" inside a string literal
    // (for example a URL) is kept: the scan only needs to ignore prose.
    private static string StripLineComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
            if (!inString && c == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static bool IsBuildOutput(string projectDir, string file)
    {
        var relative = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
        return relative.StartsWith("obj/", StringComparison.Ordinal)
            || relative.StartsWith("bin/", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedReminder.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("MedReminder.sln not found above the test output directory.");
    }
}
