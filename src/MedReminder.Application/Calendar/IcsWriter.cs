using System.Globalization;
using System.Text;

namespace MedReminder.Application.Calendar;

// Writes all-day events as an iCalendar file (RFC 5545): CRLF line
// endings, TEXT values escaped, lines folded at 75 octets of UTF-8.
// Pure formatting with no I/O, so the file export and the email
// attachment share it.
public static class IcsWriter
{
    public const string MediaType = "text/calendar";

    private const int MaxLineOctets = 75;

    public static string Write(IEnumerable<CalendarEvent> events, DateTimeOffset stamp)
    {
        ArgumentNullException.ThrowIfNull(events);
        var sb = new StringBuilder();
        Line(sb, "BEGIN:VCALENDAR");
        Line(sb, "VERSION:2.0");
        Line(sb, "PRODID:-//MedReminder//Calendar export//EN");
        Line(sb, "CALSCALE:GREGORIAN");
        Line(sb, "METHOD:PUBLISH");
        var dtstamp = stamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var e in events)
        {
            Line(sb, "BEGIN:VEVENT");
            Line(sb, "UID:" + Escape(e.Uid));
            Line(sb, "DTSTAMP:" + dtstamp);
            Line(sb, "DTSTART;VALUE=DATE:" + Date(e.Date));
            Line(sb, "DTEND;VALUE=DATE:" + Date(e.Date.AddDays(1)));
            Line(sb, "SUMMARY:" + Escape(e.Summary));
            if (!string.IsNullOrEmpty(e.Description)) Line(sb, "DESCRIPTION:" + Escape(e.Description));
            // A reminder date, not a busy slot.
            Line(sb, "TRANSP:TRANSPARENT");
            Line(sb, "END:VEVENT");
        }
        Line(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static string Date(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    // RFC 5545 §3.3.11: backslash, semicolon, comma and line breaks.
    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case ';': sb.Append("\\;"); break;
                case ',': sb.Append("\\,"); break;
                case '\r':
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    sb.Append("\\n");
                    break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    // RFC 5545 §3.1: a line longer than 75 octets continues on the next
    // line after CRLF and one space, never splitting a UTF-8 sequence.
    private static void Line(StringBuilder sb, string line)
    {
        var octets = 0;
        var limit = MaxLineOctets;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                sb.Append("\r\n ");
                octets = 0;
                // The leading space counts toward the next line.
                limit = MaxLineOctets - 1;
            }
            sb.Append(rune.ToString());
            octets += size;
        }
        sb.Append("\r\n");
    }
}
