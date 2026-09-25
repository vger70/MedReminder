using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Abstractions;

// Guards the C.3+ additive fields on BackupSettings (§3.1) — an older
// backup.settings.json missing them must deserialize to the defaults
// (cloud target off), and every new field must survive a round-trip
// through System.Text.Json (the same serializer SettingsDialog uses
// when it writes backup.settings.json).
public class BackupSettingsBindingTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Old_json_without_cloud_fields_binds_to_defaults()
    {
        var json = """
        {
          "Enabled": true,
          "Directory": "C:/backups",
          "PreferredTime": "03:00",
          "RetentionDays": 30
        }
        """;

        var settings = JsonSerializer.Deserialize<BackupSettings>(json, _options)!;

        settings.Enabled.Should().BeTrue();
        settings.Directory.Should().Be("C:/backups");
        settings.CloudFolderEnabled.Should().BeFalse();
        settings.CloudFolderDirectory.Should().BeEmpty();
        settings.CloudFolderRetention.Should().Be(30);
    }

    [Fact]
    public void Json_with_cloud_fields_round_trips()
    {
        var json = """
        {
          "Enabled": false,
          "Directory": "",
          "PreferredTime": "02:15",
          "RetentionDays": 14,
          "CloudFolderEnabled": true,
          "CloudFolderDirectory": "C:/Users/me/OneDrive/MR",
          "CloudFolderRetention": 60
        }
        """;

        var settings = JsonSerializer.Deserialize<BackupSettings>(json, _options)!;

        settings.CloudFolderEnabled.Should().BeTrue();
        settings.CloudFolderDirectory.Should().Be("C:/Users/me/OneDrive/MR");
        settings.CloudFolderRetention.Should().Be(60);
        settings.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Serialized_settings_round_trip()
    {
        var settings = new BackupSettings
        {
            Enabled = true,
            Directory = "C:/backups",
            PreferredTime = "04:00",
            RetentionDays = 45,
            CloudFolderEnabled = true,
            CloudFolderDirectory = "D:/cloud",
            CloudFolderRetention = 20,
        };

        var json = JsonSerializer.Serialize(settings);
        var read = JsonSerializer.Deserialize<BackupSettings>(json, _options)!;

        read.CloudFolderEnabled.Should().BeTrue();
        read.CloudFolderDirectory.Should().Be("D:/cloud");
        read.CloudFolderRetention.Should().Be(20);
    }
}
