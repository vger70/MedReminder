using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using MedReminder.Infrastructure.Email;
using MedReminder.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Settings;

// Household step H2b: the settings files keep the shape the Settings
// dialog wrote, so the configuration binds them as before.
public sealed class InstallationSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mr-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private InstallationSettingsStore Store() => new(
        new Monitor<SmtpSettings>(new SmtpSettings()), new Monitor<BackupSettings>(new BackupSettings()),
        new Monitor<UserSettings>(new UserSettings()), new Location(_directory));

    [Fact]
    public void Each_section_is_written_under_its_name()
    {
        var store = Store();
        store.WriteSmtp(new SmtpTransport("smtp.example.org", 465, false, "anna", "anna@example.org", "Anna", 20));
        store.WriteBackup(new BackupSettings { Enabled = true, Directory = @"D:\Backups" });
        store.WriteUser(new UserSettings { Language = "it", ReferenceCountry = "IT" });

        Read("smtp.settings.json").GetProperty("Smtp").GetProperty("Port").GetInt32().Should().Be(465);
        Read("backup.settings.json").GetProperty("Backup").GetProperty("Directory").GetString().Should().Be(@"D:\Backups");
        Read("user.settings.json").GetProperty("UI").GetProperty("Language").GetString().Should().Be("it");
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    private JsonElement Read(string name)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, name))).RootElement;

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class Location(string directory) : IAppDataLocation
    {
        public string DataDirectory => directory;
    }
}
