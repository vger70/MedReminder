using FluentAssertions;
using MedReminder.Infrastructure.Localization;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Localization;

// LocalizationService reads user overrides from the data directory it is
// given (IAppDataLocation), not from %LOCALAPPDATA% directly.
public sealed class LocalizationServiceTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "medreminder-loc-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Override_in_the_data_directory_wins()
    {
        Directory.CreateDirectory(Path.Combine(_dataDirectory, "localization"));
        File.WriteAllText(
            Path.Combine(_dataDirectory, "localization", "strings.en.json"),
            """{ "Test.Only.Key": "from override" }""");

        var loc = LocalizationService.CreateStandalone("en", _dataDirectory);

        loc.Get("Test.Only.Key").Should().Be("from override");
    }

    [Fact]
    public void Without_a_data_directory_missing_keys_fall_back_to_the_key()
    {
        var loc = LocalizationService.CreateStandalone("en", dataDirectory: null);

        loc.Get("Test.Only.Key").Should().Be("[Test.Only.Key]");
        loc.CurrentLanguage.Should().Be("en");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDirectory, recursive: true); } catch { /* best effort */ }
    }
}
