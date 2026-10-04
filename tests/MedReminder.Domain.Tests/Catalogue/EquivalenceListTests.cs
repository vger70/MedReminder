using FluentAssertions;
using MedReminder.Domain.Catalogue;
using Xunit;

namespace MedReminder.Domain.Tests.Catalogue;

// Equivalents list (docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2).
public class EquivalenceListTests
{
    private static EquivalenceList List() => new(CountryCode.Parse("IT"), new DateOnly(2026, 9, 15),
    [
        new EquivalenceGroup("12A", "NIFEDIPINA", "14 UNITA' 30 MG - USO ORALE", "C08CA05", 5.12m,
        [
            new EquivalentPackage("026622050", "ADALAT CRONO", "14 CPR", "BAYER", 6.20m, 1.08m, null),
            new EquivalentPackage("037248022", "NIFEDIPINA EG", "14 CPR", "EG", 5.12m, 0m, null),
            new EquivalentPackage("035831015", "NIFEDIPINA DOC*", "14 CPR", "DOC", 5.12m, 0m, "*non sostituibile con Adalat Crono"),
            new EquivalentPackage("039274016", "NIFEDIPINA X", "14 CPR", "X", null, null, " "),
        ]),
        new EquivalenceGroup("810", "SALBUTAMOLO", "200 DOSI 100 MCG - USO INALATORIO", "R03AC02", 3.50m,
        [
            new EquivalentPackage("022276012", "VENTOLIN", "200 DOSI", "GSK", 3.90m, 0.40m, null),
        ]),
    ]);

    [Fact]
    public void A_package_code_finds_its_group()
    {
        var list = List();

        list.FindGroup("035831015")!.Code.Should().Be("12A");
        list.FindGroup(" 022276012 ")!.Code.Should().Be("810", "codes are trimmed");
        list.FindGroup("999999999").Should().BeNull();
        list.FindGroup(null).Should().BeNull();
        list.GroupByCode("810")!.ActiveIngredient.Should().Be("SALBUTAMOLO");
        list.GroupCount.Should().Be(2);
        list.PackageCount.Should().Be(5);
    }

    [Fact]
    public void Members_by_price_are_cheapest_first_and_unpriced_last()
    {
        List().GroupByCode("12A")!.MembersByPrice().Select(m => m.Code)
            .Should().Equal("035831015", "037248022", "026622050", "039274016");
    }

    [Fact]
    public void A_note_is_kept_verbatim_and_a_blank_one_is_no_note()
    {
        var group = List().GroupByCode("12A")!;

        group.Members.Single(m => m.Code == "035831015").HasNote.Should().BeTrue();
        group.Members.Single(m => m.Code == "035831015").Note.Should().Be("*non sostituibile con Adalat Crono");
        group.Members.Single(m => m.Code == "039274016").HasNote.Should().BeFalse();
        group.Contains("026622050").Should().BeTrue();
        group.Contains("022276012").Should().BeFalse();
    }
}
