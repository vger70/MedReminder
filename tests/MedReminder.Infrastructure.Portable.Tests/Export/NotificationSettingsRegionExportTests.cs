using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Infrastructure.Export;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// The profile's region is an additive field of notificationSettings
// (docs/EXPORT-FORMAT.md §3.9, §5; PROMPT-REGIONAL-PRESCRIPTION-SERVICES
// §3.2).
public class NotificationSettingsRegionExportTests
{
    [Fact]
    public void The_region_round_trips()
    {
        var json = JsonSerializer.Serialize(new ExportedNotificationSettings { ToAddress = "a@example.org", Region = "12" },
            ExportJson.Options);

        json.Should().Contain("\"region\": \"12\"");
        JsonSerializer.Deserialize<ExportedNotificationSettings>(json, ExportJson.Options)!.Region.Should().Be("12");
    }

    [Fact]
    public void An_archive_without_the_region_imports_no_region()
    {
        const string json = "{\"toAddress\":\"a@example.org\",\"caregiverAddress\":\"\",\"doctorAddress\":\"\"}";

        var settings = JsonSerializer.Deserialize<ExportedNotificationSettings>(json, ExportJson.Options)!;

        settings.Region.Should().BeEmpty();
        settings.ToAddress.Should().Be("a@example.org");
    }
}
