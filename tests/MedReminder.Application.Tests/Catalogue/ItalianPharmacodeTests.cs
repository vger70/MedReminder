using FluentAssertions;
using MedReminder.Application.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class ItalianPharmacodeTests
{
    [Theory]
    // BWIPP code32 example: 01234567 -> check digit 6.
    [InlineData("01234567", 6)]
    // Real AIC codes from the shipped AIFA snapshot.
    [InlineData("01274509", 3)]
    [InlineData("02392104", 8)]
    [InlineData("00015203", 7)]
    public void Computes_check_digit(string firstEight, int expected)
    {
        ItalianPharmacode.ComputeCheckDigit(firstEight).Should().Be(expected);
    }

    [Theory]
    [InlineData("012345676", true)]
    [InlineData("012745093", true)]
    [InlineData("012745094", false)]
    [InlineData("01274509", false)]
    [InlineData("0127450930", false)]
    [InlineData("01274509X", false)]
    [InlineData("", false)]
    public void Validates_aic(string aic, bool expected)
    {
        ItalianPharmacode.IsValidAic(aic).Should().Be(expected);
    }

    [Theory]
    [InlineData("0CSSBD", "012345676")]
    [InlineData("0D4YD5", "012745093")]
    [InlineData("0d4yd5", "012745093")]
    [InlineData("0QU0DS", "023921048")]
    [InlineData("004NH5", "000152037")]
    public void Decodes_code32(string code32, string expectedAic)
    {
        ItalianPharmacode.TryDecodeCode32(code32).Should().Be(expectedAic);
    }

    [Theory]
    // Vowels A, E, I, O are not in the Code 32 alphabet.
    [InlineData("0D4YE5")]
    [InlineData("0A4YD5")]
    // Decodes to 012745094: wrong check digit.
    [InlineData("0D4YD6")]
    // 1,073,741,823: more than nine digits.
    [InlineData("ZZZZZZ")]
    [InlineData("0D4YD")]
    [InlineData("0D4YD55")]
    [InlineData("0D4-D5")]
    public void Rejects_invalid_code32(string code32)
    {
        ItalianPharmacode.TryDecodeCode32(code32).Should().BeNull();
    }
}
