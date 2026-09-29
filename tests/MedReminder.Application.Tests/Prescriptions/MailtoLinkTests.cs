using FluentAssertions;
using MedReminder.Application.Prescriptions;
using Xunit;

namespace MedReminder.Application.Tests.Prescriptions;

public class MailtoLinkTests
{
    [Fact]
    public void Encodes_subject_and_body_with_crlf_line_breaks()
    {
        var ok = MailtoLink.TryBuild(
            "doctor@example.org", "Request: A&B", "Line 1\nLine 2?", out var uri);

        ok.Should().BeTrue();
        uri.Should().Be(
            "mailto:doctor@example.org"
            + "?subject=Request%3A%20A%26B"
            + "&body=Line%201%0D%0ALine%202%3F");
    }

    [Fact]
    public void Does_not_double_existing_crlf()
    {
        MailtoLink.TryBuild(null, "s", "a\r\nb", out var uri).Should().BeTrue();

        uri.Should().EndWith("&body=a%0D%0Ab");
    }

    [Fact]
    public void Leaves_the_address_empty_when_no_recipient()
    {
        MailtoLink.TryBuild("  ", "s", "b", out var uri).Should().BeTrue();

        uri.Should().Be("mailto:?subject=s&body=b");
    }

    [Fact]
    public void Encodes_reserved_characters_in_the_address_but_keeps_the_at_sign()
    {
        MailtoLink.TryBuild("a?b&c@example.org", "s", "b", out var uri).Should().BeTrue();

        uri.Should().StartWith("mailto:a%3Fb%26c@example.org?");
    }

    [Fact]
    public void Refuses_a_uri_longer_than_the_limit()
    {
        var body = new string('x', MailtoLink.MaxLength);

        var ok = MailtoLink.TryBuild("doctor@example.org", "s", body, out var uri);

        ok.Should().BeFalse();
        uri.Should().BeNull();
    }

    [Fact]
    public void Accepts_a_uri_exactly_at_the_limit()
    {
        const string prefix = "mailto:?subject=s&body=";
        var body = new string('x', MailtoLink.MaxLength - prefix.Length);

        MailtoLink.TryBuild(null, "s", body, out var uri).Should().BeTrue();

        uri!.Length.Should().Be(MailtoLink.MaxLength);
    }
}
