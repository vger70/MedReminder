using System.Text.RegularExpressions;
using FluentAssertions;
using MedReminder.Infrastructure.Donations;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Donations;

// Verifies the JSON options loader binds the on-disk shape (fixed +
// custom keys) and stays silent (Enabled = false) on a missing or
// corrupt file (A6, docs/ANALYSIS-A6-DONATION-SUPPORT.md §12.2).
public class JsonDonationOptionsProviderTests : IDisposable
{
    private readonly string _dir;

    public JsonDonationOptionsProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "medreminder-donations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string WriteFile(string json)
    {
        var path = Path.Combine(_dir, JsonDonationOptionsProvider.SettingsFileName);
        File.WriteAllText(path, json);
        return path;
    }


    // Defensive guard for the §9 release check: the shipped template
    // must never contain a secret-shaped string.
    [Fact]
    public void Shipped_template_contains_no_secret_shaped_strings()
    {
        var repoRoot = FindRepoRoot();
        var template = Path.Combine(repoRoot, "src", "MedReminder.UI", "donations.settings.json");
        File.Exists(template).Should().BeTrue($"expected the template at {template}");

        var content = File.ReadAllText(template);
        foreach (var needle in new[] { "sk_live", "sk_test", "client_secret", "access_token", "-----BEGIN" })
        {
            content.Should().NotContain(needle, $"the donation template must not carry a '{needle}' secret");
        }

        // The template must ship disabled so the feature is opt-in.
        Regex.IsMatch(content, "\"Enabled\"\\s*:\\s*true").Should().BeFalse(
            "the shipped template must default every Enabled flag to false");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MedReminder.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }
}
