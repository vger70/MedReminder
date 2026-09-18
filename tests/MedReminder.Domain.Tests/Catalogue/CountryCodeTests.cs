using FluentAssertions;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Domain.Tests.Catalogue;

public sealed class CountryCodeTests
{
    [Theory]
    [InlineData("IT")]
    [InlineData("it")]
    [InlineData(" IT ")]
    [InlineData("Es")]
    public void Parse_normalises_iso_alpha2_to_upper_case(string raw)
    {
        var code = CountryCode.Parse(raw);

        code.Value.Should().Be(raw.Trim().ToUpperInvariant());
        code.IsSupranational.Should().BeFalse();
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("eu")]
    [InlineData("European Union")]
    [InlineData("european union")]
    public void Parse_normalises_supranational_long_form_to_EU(string raw)
    {
        var code = CountryCode.Parse(raw);

        code.Value.Should().Be("EU");
        code.IsSupranational.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I")]
    [InlineData("ITA")]
    [InlineData("I1")]
    [InlineData("1T")]
    [InlineData("European")]
    public void Parse_rejects_invalid_input(string raw)
    {
        var act = () => CountryCode.Parse(raw);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_rejects_null()
    {
        var act = () => CountryCode.Parse(null!);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_returns_false_for_invalid_input()
    {
        CountryCode.TryParse("XYZ", out _).Should().BeFalse();
    }

    [Fact]
    public void Two_countries_with_same_value_are_equal()
    {
        var a = CountryCode.Parse("IT");
        var b = CountryCode.Parse("it");

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
