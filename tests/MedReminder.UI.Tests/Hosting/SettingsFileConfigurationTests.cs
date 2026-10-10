using FluentAssertions;
using MedReminder.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MedReminder.UI.Tests.Hosting;

// A damaged settings file is skipped instead of stopping the
// configuration, and so the start of the app.
public sealed class SettingsFileConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsFileConfigurationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_valid_file_is_loaded()
    {
        var path = Write("user.settings.json", """{ "UI": { "Language": "de" } }""");

        var configuration = new ConfigurationBuilder().AddSettingsFile(path, reloadOnChange: false).Build();

        configuration["UI:Language"].Should().Be("de");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public void A_damaged_file_is_skipped(string content)
    {
        var damaged = Write("user.settings.json", content);
        var valid = Write("backup.settings.json", """{ "Backup": { "Enabled": true } }""");

        var configuration = new ConfigurationBuilder()
            .AddSettingsFile(damaged, reloadOnChange: false)
            .AddSettingsFile(valid, reloadOnChange: false)
            .Build();

        configuration["UI:Language"].Should().BeNull();
        configuration["Backup:Enabled"].Should().Be("True");
    }

    [Fact]
    public void A_locked_file_is_skipped()
    {
        var path = Write("user.settings.json", """{ "UI": { "Language": "de" } }""");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var configuration = new ConfigurationBuilder().AddSettingsFile(path, reloadOnChange: false).Build();

        configuration["UI:Language"].Should().BeNull();
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}
