using FluentAssertions;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Domain.Tests.Catalogue;

public sealed class AtcCodeTests
{
    [Theory]
    [InlineData("A10BA02")] // metformin
    [InlineData("N02BE01")] // paracetamol
    [InlineData("J01CR02")] // amoxicillin + clavulanic acid
    public void Parse_accepts_valid_seven_char_codes(string raw)
    {
        var atc = AtcCode.Parse(raw);

        atc.Value.Should().Be(raw);
    }

    [Theory]
    [InlineData("a10ba02")]
    [InlineData(" A10BA02 ")]
    public void Parse_normalises_case_and_whitespace(string raw)
    {
        var atc = AtcCode.Parse(raw);

        atc.Value.Should().Be("A10BA02");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A10")]         // shorter administrative level
    [InlineData("A10B")]        // shorter administrative level
    [InlineData("A10BA")]       // shorter administrative level
    [InlineData("A10BA002")]    // too long
    [InlineData("110BA02")]     // starts with digit
    [InlineData("A1BBA02")]     // second char not a digit
    [InlineData("A10BAAA")]     // trailing letters instead of digits
    [InlineData("A10-BA02")]    // separator
    public void Parse_rejects_invalid_input(string raw)
    {
        var act = () => AtcCode.Parse(raw);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_returns_false_for_invalid_input()
    {
        AtcCode.TryParse("nope", out _).Should().BeFalse();
    }

    [Fact]
    public void Two_codes_with_same_value_are_equal()
    {
        var a = AtcCode.Parse("A10BA02");
        var b = AtcCode.Parse("a10ba02");

        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
