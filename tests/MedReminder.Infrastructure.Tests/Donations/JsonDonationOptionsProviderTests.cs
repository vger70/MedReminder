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

    [Fact]
    public void Binds_fixed_and_custom_keys_from_sample_json()
    {
        var path = WriteFile("""
        {
          "Donations": {
            "Enabled": true,
            "Currency": "EUR",
            "Stripe": {
              "Enabled": true,
              "PaymentLinks": {
                "2": "https://donate.stripe.com/two",
                "5": "https://donate.stripe.com/five",
                "custom": "https://donate.stripe.com/choose"
              }
            },
            "PayPal": {
              "Enabled": false,
              "PaymentLinks": {}
            }
          }
        }
        """);

        var options = JsonDonationOptionsProvider.Load(path);

        options.Enabled.Should().BeTrue();
        options.Currency.Should().Be("EUR");
        options.Stripe.Enabled.Should().BeTrue();
        options.Stripe.PaymentLinks.Should().ContainKey("2")
            .WhoseValue.Should().Be("https://donate.stripe.com/two");
        options.Stripe.PaymentLinks.Should().ContainKey("custom")
            .WhoseValue.Should().Be("https://donate.stripe.com/choose");
        options.PayPal.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Missing_file_binds_to_disabled()
    {
        var path = Path.Combine(_dir, "does-not-exist.json");

        var options = JsonDonationOptionsProvider.Load(path);

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Missing_donations_section_binds_to_disabled()
    {
        var path = WriteFile("""{ "Something": { "Else": true } }""");

        var options = JsonDonationOptionsProvider.Load(path);

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Corrupt_file_binds_to_disabled()
    {
        var path = WriteFile("{ this is not valid json ");

        var options = JsonDonationOptionsProvider.Load(path);

        options.Enabled.Should().BeFalse();
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
