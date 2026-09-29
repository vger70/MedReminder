using FluentAssertions;
using MedReminder.Application.Catalogue;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Application.Tests.Catalogue;

public sealed class BarcodeParserTests
{
    private const char Gs = '\u001D';
    private const string Gtin = "08001234567897";

    private static BarcodeContent Parse(string payload, BarcodeSymbology symbology = BarcodeSymbology.Unknown) =>
        new BarcodeParser().Parse(new RawBarcode(symbology, payload));

    // ---------------- GS1 DataMatrix ----------------

    [Fact]
    public void DataMatrix_with_separators_yields_every_element()
    {
        var content = Parse($"01{Gtin}21SERIAL123{Gs}1727123110LOT42", BarcodeSymbology.DataMatrix);

        content.Gtin.Should().Be(Gtin);
        content.Serial.Should().Be("SERIAL123");
        content.Expiry.Should().Be(new DateOnly(2027, 12, 31));
        content.Batch.Should().Be("LOT42");
        content.NationalCode.Should().BeNull();
    }

    [Fact]
    public void DataMatrix_is_recognized_without_symbology_hint()
    {
        var content = Parse($"01{Gtin}21SERIAL123{Gs}1727123110LOT42");

        content.Gtin.Should().Be(Gtin);
        content.Batch.Should().Be("LOT42");
    }

    [Theory]
    [InlineData("]d2")]
    [InlineData("]d1")]
    public void DataMatrix_aim_prefix_is_stripped(string prefix)
    {
        var content = Parse($"{prefix}01{Gtin}21SERIAL123{Gs}1727123110LOT42");

        content.Gtin.Should().Be(Gtin);
        content.Serial.Should().Be("SERIAL123");
    }

    [Fact]
    public void Visible_group_separator_glyph_is_mapped_back()
    {
        var content = Parse($"01{Gtin}21SERIAL123{BarcodeParser.GroupSeparatorGlyph}1727123110LOT42");

        content.Serial.Should().Be("SERIAL123");
        content.Batch.Should().Be("LOT42");
    }

    [Fact]
    public void Configured_substitute_is_mapped_to_group_separator()
    {
        var parser = new BarcodeParser(groupSeparatorSubstitute: '~');

        var content = parser.Parse(new RawBarcode(BarcodeSymbology.Unknown, $"01{Gtin}21SERIAL123~1727123110LOT42"));

        content.Serial.Should().Be("SERIAL123");
        content.Batch.Should().Be("LOT42");
    }

    [Fact]
    public void Substitute_is_read_from_options()
    {
        var parser = BarcodeParser.FromOptions(new BarcodeCaptureOptions { HidGroupSeparatorSubstitute = "~" });

        var content = parser.Parse(new RawBarcode(BarcodeSymbology.Unknown, $"01{Gtin}21SERIAL123~1727123110LOT42"));

        content.Serial.Should().Be("SERIAL123");
    }

    [Fact]
    public void Without_separators_only_unambiguous_elements_are_kept()
    {
        var content = Parse($"01{Gtin}21SERIAL1231727123110LOT42");

        content.Gtin.Should().Be(Gtin);
        content.Serial.Should().BeNull();
        content.Expiry.Should().BeNull();
        content.Batch.Should().BeNull();
    }

    [Fact]
    public void Without_separators_fixed_elements_before_variable_ones_are_kept()
    {
        var content = Parse($"01{Gtin}1727123110LOT4221SERIAL123");

        content.Gtin.Should().Be(Gtin);
        content.Expiry.Should().Be(new DateOnly(2027, 12, 31));
        content.Batch.Should().BeNull();
        content.Serial.Should().BeNull();
    }

    [Fact]
    public void Expiry_day_zero_means_last_day_of_month()
    {
        var content = Parse($"01{Gtin}17270200");

        content.Expiry.Should().Be(new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void Invalid_expiry_keeps_the_gtin()
    {
        var content = Parse($"01{Gtin}17271301");

        content.Gtin.Should().Be(Gtin);
        content.Expiry.Should().BeNull();
    }

    [Theory]
    // Wrong GTIN check digit.
    [InlineData("0108001234567890")]
    // Non-digit inside AI 01.
    [InlineData("01080012345678X7")]
    // Truncated AI 01.
    [InlineData("01080012345678")]
    // Does not start with AI 01.
    [InlineData("10LOT42")]
    public void Malformed_DataMatrix_is_unrecognized(string payload)
    {
        Parse(payload, BarcodeSymbology.DataMatrix).Should().Be(BarcodeContent.Unrecognized);
    }

    [Fact]
    public void Variable_field_longer_than_twenty_characters_is_dropped()
    {
        var content = Parse($"01{Gtin}21{new string('X', 21)}{Gs}10LOT42");

        content.Gtin.Should().Be(Gtin);
        content.Serial.Should().BeNull();
    }

    // ---------------- Code 32 / AIC ----------------

    [Theory]
    [InlineData("0D4YD5")]
    [InlineData("A012745093")]
    [InlineData("a012745093")]
    [InlineData("012745093")]
    public void Every_aic_form_yields_the_national_code(string payload)
    {
        var content = Parse(payload);

        content.NationalCode.Should().Be("012745093");
        content.Gtin.Should().BeNull();
        content.HasLookupKey.Should().BeTrue();
        content.LookupKey.Should().Be("012745093");
    }

    [Theory]
    [InlineData("0D4YD5")]
    [InlineData("A012745093")]
    [InlineData("012745093")]
    public void Code39_symbology_accepts_every_aic_form(string payload)
    {
        Parse(payload, BarcodeSymbology.Code39).NationalCode.Should().Be("012745093");
    }

    [Fact]
    public void Code39_aim_prefix_is_stripped()
    {
        Parse("]A00D4YD5").NationalCode.Should().Be("012745093");
    }

    [Theory]
    [InlineData("012745094")]
    [InlineData("A012745094")]
    [InlineData("0D4YD6")]
    [InlineData("0D4YE5")]
    [InlineData("B012745093")]
    public void Aic_with_wrong_check_digit_or_alphabet_is_unrecognized(string payload)
    {
        Parse(payload).Should().Be(BarcodeContent.Unrecognized);
    }

    // ---------------- EAN-13 ----------------

    [Fact]
    public void Ean13_yields_a_padded_gtin_and_no_national_code()
    {
        var content = Parse("8001234567897");

        content.Gtin.Should().Be("08001234567897");
        content.NationalCode.Should().BeNull();
        content.LookupKey.Should().Be("08001234567897");
    }

    [Fact]
    public void Ean13_aim_prefix_is_stripped()
    {
        Parse("]E08001234567897").Gtin.Should().Be("08001234567897");
    }

    [Theory]
    [InlineData("8001234567890")]
    [InlineData("800123456789")]
    [InlineData("80012345678970")]
    public void Invalid_ean13_is_unrecognized(string payload)
    {
        Parse(payload, BarcodeSymbology.Ean13).Should().Be(BarcodeContent.Unrecognized);
    }

    [Fact]
    public void Ean13_symbology_does_not_accept_an_aic()
    {
        Parse("012745093", BarcodeSymbology.Ean13).Should().Be(BarcodeContent.Unrecognized);
    }

    // ---------------- Keyboard-wedge artifacts ----------------

    [Theory]
    [InlineData("A012745093\r")]
    [InlineData("A012745093\r\n")]
    [InlineData("A012745093\t")]
    [InlineData("  A012745093 ")]
    public void Scanner_suffixes_and_whitespace_are_ignored(string payload)
    {
        Parse(payload).NationalCode.Should().Be("012745093");
    }

    [Fact]
    public void Azerty_garbled_digits_are_unrecognized()
    {
        // "012745093" typed by a US-configured scanner on a French
        // AZERTY layout.
        Parse("à&éè'(àç\"").Should().Be(BarcodeContent.Unrecognized);
    }

    // ---------------- Robustness ----------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData("]d2")]
    [InlineData("]")]
    public void Empty_or_prefix_only_payloads_are_unrecognized(string payload)
    {
        Parse(payload).Should().Be(BarcodeContent.Unrecognized);
    }

    [Fact]
    public void Oversized_payload_is_unrecognized()
    {
        Parse(new string('1', 257)).Should().Be(BarcodeContent.Unrecognized);
    }

    [Fact]
    public void Null_payload_is_unrecognized()
    {
        new BarcodeParser().Parse(default).Should().Be(BarcodeContent.Unrecognized);
    }

    // Seeded pseudo-random payloads (control characters included):
    // the parser must never throw, whatever it receives.
    [Fact]
    public void Random_payloads_never_throw()
    {
        var random = new Random(20260926);
        var parser = new BarcodeParser(groupSeparatorSubstitute: '~');
        foreach (var symbology in Enum.GetValues<BarcodeSymbology>())
        {
            for (var i = 0; i < 2_000; i++)
            {
                var chars = new char[random.Next(0, 65)];
                for (var j = 0; j < chars.Length; j++)
                {
                    chars[j] = random.Next(4) switch
                    {
                        0 => (char)random.Next('0', '9' + 1),
                        1 => (char)random.Next(0, 0x20),
                        2 => "]d2AE0117102101~␝"[random.Next(17)],
                        _ => (char)random.Next(0x20, 0x250),
                    };
                }

                var act = () => parser.Parse(new RawBarcode(symbology, new string(chars)));

                act.Should().NotThrow();
            }
        }
    }
}
