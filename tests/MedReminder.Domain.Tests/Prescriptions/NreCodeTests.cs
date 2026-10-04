using FluentAssertions;
using MedReminder.Domain.Catalogue;
using MedReminder.Domain.Prescriptions;
using Xunit;

namespace MedReminder.Domain.Tests.Prescriptions;

// docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.2, §6.
public class NreCodeTests
{
    [Theory]
    [InlineData("1200A4567890123", "1200A4567890123")]
    [InlineData("120 0A 4 5678901 23", "1200A4567890123")]
    [InlineData("12-00A-4567890123", "1200A4567890123")]
    [InlineData("  1200a4567890123\r\n", "1200A4567890123")]
    [InlineData("120\t0a4 - 567890123", "1200A4567890123")]
    public void Spaces_dashes_and_lower_case_are_normalised(string text, string expected)
    {
        NreCode.TryParse(text, out var code).Should().BeTrue();
        code.Value.Should().Be(expected);
        code.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1200A456789012")]
    [InlineData("1200A45678901234")]
    [InlineData("1200A4567890.23")]
    [InlineData("1200A4567890_23")]
    [InlineData("1200A456789012è")]
    [InlineData("1200A4567890１２３")]
    public void Anything_else_is_refused(string? text)
    {
        NreCode.TryParse(text, out var code).Should().BeFalse();
        code.Should().Be(default(NreCode));
    }
}

public class RegionalServicesListTests
{
    private static RegionalHealthService Service(string code, string web, string? ios = null) => new(
        code, "Region " + code, "Service " + code, new Uri(web), ios is null ? null : new Uri(ios), null,
        [SignInMethod.Spid], true, false, new DateOnly(2026, 10, 4));

    [Fact]
    public void Finds_by_code_and_lists_the_links_of_an_entry()
    {
        var list = new RegionalServicesList(CountryCode.Parse("IT"), new DateOnly(2026, 10, 4),
        [
            Service("12", "https://www.salutelazio.it/", "https://apps.apple.com/it/app/x/id1"),
            Service("05", "https://www.sanitakmzero.it/"),
        ]);

        list.Count.Should().Be(2);
        list.Find(" 12 ")!.Service.Should().Be("Service 12");
        list.Find("15").Should().BeNull();
        list.Find(null).Should().BeNull();
        list.Find("12")!.Links().Select(u => u.AbsoluteUri)
            .Should().Equal("https://www.salutelazio.it/", "https://apps.apple.com/it/app/x/id1");
        list.Find("05")!.Links().Should().ContainSingle();
    }

    [Fact]
    public void There_are_21_regions_and_provinces()
    {
        ItalianRegions.Codes.Should().HaveCount(21).And.OnlyHaveUniqueItems().And.NotContain("04");
        ItalianRegions.IsValid("12").Should().BeTrue();
        ItalianRegions.IsValid("04").Should().BeFalse();
        ItalianRegions.IsValid("12 ").Should().BeFalse();
        ItalianRegions.IsValid(null).Should().BeFalse();
    }
}
