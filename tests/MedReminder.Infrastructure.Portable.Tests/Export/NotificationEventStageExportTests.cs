using System.Text.Json;
using FluentAssertions;
using MedReminder.Application.Export;
using MedReminder.Domain.Notifications;
using MedReminder.Infrastructure.Export;
using Xunit;

namespace MedReminder.Infrastructure.Tests.Export;

// Second low-stock warning: the stage of a notification event is an
// additive field of the archive (docs/EXPORT-FORMAT.md §3.7, §5).
public class NotificationEventStageExportTests
{
    [Fact]
    public void The_stage_round_trips()
    {
        var evt = new NotificationEvent
        {
            MedicineId = Guid.NewGuid(),
            StockEpoch = 2,
            TriggeredAt = new DateTimeOffset(2026, 9, 13, 9, 0, 0, TimeSpan.Zero),
            Channel = NotificationChannels.Windows,
            DaysRemainingAtSend = 3,
            Success = true,
            Stage = 2,
        };

        ExportMapper.ToEntity(ExportMapper.ToDto(evt)).Stage.Should().Be(2);
    }

    [Fact]
    public void An_archive_without_the_stage_imports_a_first_stage_event()
    {
        const string json =
            "{\"id\":\"0f0f0f0f-0000-0000-0000-000000000002\"," +
            "\"medicineId\":\"0b0b0b0b-0000-0000-0000-000000000001\",\"stockEpoch\":2," +
            "\"triggeredAt\":\"2026-09-13T09:00:00+00:00\",\"channel\":\"Windows\"," +
            "\"daysRemainingAtSend\":3,\"success\":true,\"errorMessage\":null}";

        var dto = JsonSerializer.Deserialize<ExportedNotificationEvent>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        ExportMapper.ToEntity(dto).Stage.Should().Be(1);
    }
}
