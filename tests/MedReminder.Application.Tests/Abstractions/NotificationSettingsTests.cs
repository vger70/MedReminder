using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Abstractions;
using Xunit;

namespace MedReminder.Application.Tests.Abstractions;

// A3 (docs/analysis/ANALYSIS-A3-CAREGIVER-NOTIFICATIONS.md §9): the new
// CaregiverAddress field must be backwards-compatible with pre-A3
// notifications.settings.json files. An older file that lacks the key
// must deserialize with the default (empty) and behave as before.
public class NotificationSettingsTests
{
    [Fact]
    public void Deserializes_caregiver_address_when_present()
    {
        const string json = """
            {
              "ToAddress": "user@example.com",
              "CaregiverAddress": "caregiver@example.com"
            }
            """;

        var settings = JsonSerializer.Deserialize<NotificationSettings>(json);

        settings.Should().NotBeNull();
        settings!.ToAddress.Should().Be("user@example.com");
        settings.CaregiverAddress.Should().Be("caregiver@example.com");
    }

    [Fact]
    public void Defaults_caregiver_address_to_empty_when_key_absent()
    {
        // Pre-A3 payload: no CaregiverAddress key.
        const string json = """
            {
              "ToAddress": "user@example.com"
            }
            """;

        var settings = JsonSerializer.Deserialize<NotificationSettings>(json);

        settings.Should().NotBeNull();
        settings!.ToAddress.Should().Be("user@example.com");
        settings.CaregiverAddress.Should().BeEmpty();
    }
}
