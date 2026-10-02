using System.Text;
using FluentAssertions;
using MedReminder.Application.Calendar;
using Xunit;

namespace MedReminder.Application.Tests.Calendar;

// RFC 5545 form of the calendar export (docs/notes/
// EVOLUTION-PROPOSALS-2.md §3.7).
public class IcsWriterTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void An_all_day_event_is_written_with_crlf_lines()
    {
        var ics = IcsWriter.Write(
            [new CalendarEvent("runout-1@medreminder", new DateOnly(2026, 10, 31), "Runs out", "Details")], Stamp);

        ics.Should().Be(
            "BEGIN:VCALENDAR\r\n" +
            "VERSION:2.0\r\n" +
            "PRODID:-//MedReminder//Calendar export//EN\r\n" +
            "CALSCALE:GREGORIAN\r\n" +
            "METHOD:PUBLISH\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:runout-1@medreminder\r\n" +
            "DTSTAMP:20261001T073000Z\r\n" +
            "DTSTART;VALUE=DATE:20261031\r\n" +
            "DTEND;VALUE=DATE:20261101\r\n" +
            "SUMMARY:Runs out\r\n" +
            "DESCRIPTION:Details\r\n" +
            "TRANSP:TRANSPARENT\r\n" +
            "END:VEVENT\r\n" +
            "END:VCALENDAR\r\n");
    }

    [Fact]
    public void Text_values_are_escaped()
    {
        IcsWriter.Write([new CalendarEvent("u", new DateOnly(2026, 1, 1), "a\\b;c,d\r\ne\nf")], Stamp)
            .Should().Contain(@"SUMMARY:a\\b\;c\,d\ne\nf" + "\r\n");
    }

    [Fact]
    public void Long_lines_are_folded_at_75_octets_without_splitting_a_character()
    {
        var summary = new string('à', 60); // 120 octets in UTF-8
        var ics = IcsWriter.Write([new CalendarEvent("u", new DateOnly(2026, 1, 1), summary)], Stamp);

        var lines = ics.Split("\r\n");
        lines.Should().OnlyContain(l => Encoding.UTF8.GetByteCount(l) <= 75);
        var start = Array.FindIndex(lines, l => l.StartsWith("SUMMARY:", StringComparison.Ordinal));
        var unfolded = lines[start] + string.Concat(lines.Skip(start + 1).TakeWhile(l => l.StartsWith(' ')).Select(l => l[1..]));
        unfolded.Should().Be("SUMMARY:" + summary);
    }

    [Fact]
    public void An_empty_calendar_is_still_valid()
    {
        IcsWriter.Write([], Stamp).Should().StartWith("BEGIN:VCALENDAR\r\n").And.EndWith("END:VCALENDAR\r\n")
            .And.NotContain("VEVENT");
    }
}
