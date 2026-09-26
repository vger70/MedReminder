namespace MedReminder.Application.Prescriptions;

// Builds an RFC 6068 mailto: URI for the "Open in mail client" action
// of the prescription request. Pure, so the encoding and the length
// guard are unit-tested without a shell.
public static class MailtoLink
{
    // Conservative ceiling for the whole URI. Mail clients and the
    // Windows shell hand-off truncate or reject long mailto: URIs at
    // different lengths; staying well below the common limits keeps
    // the draft intact. Above it the caller falls back to the
    // clipboard.
    public const int MaxLength = 2000;

    // Returns false (and uri = null) when the encoded URI exceeds
    // MaxLength. The recipient may be empty: the mail client then
    // opens the draft with an empty To field.
    public static bool TryBuild(string? recipient, string subject, string body, out string? uri)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        // RFC 6068 §5: line breaks in the body are encoded as %0D%0A.
        var normalizedBody = body.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

        // '@' stays literal: some clients do not decode %40 in the
        // address part. Everything else ('?', '&', '%', '#', ...) is
        // percent-encoded so it cannot break the URI structure.
        var to = string.IsNullOrWhiteSpace(recipient)
            ? string.Empty
            : Uri.EscapeDataString(recipient.Trim()).Replace("%40", "@");

        var candidate = "mailto:" + to
            + "?subject=" + Uri.EscapeDataString(subject)
            + "&body=" + Uri.EscapeDataString(normalizedBody);

        if (candidate.Length > MaxLength)
        {
            uri = null;
            return false;
        }

        uri = candidate;
        return true;
    }
}
