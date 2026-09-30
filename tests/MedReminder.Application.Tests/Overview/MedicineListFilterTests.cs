using FluentAssertions;
using MedReminder.Application.Overview;
using Xunit;

namespace MedReminder.Application.Tests.Overview;

public sealed class MedicineListFilterTests
{
    private static MedicineListItem Row(string name, MedicineRowStatus status, bool active = true)
        => new() { Id = Guid.NewGuid(), Name = name, Status = status, IsActive = active };

    private static readonly IReadOnlyList<MedicineListItem> Rows =
    [
        Row("Eutirox 50 mcg", MedicineRowStatus.Empty),
        Row("Cardioaspirin 100 mg", MedicineRowStatus.Warning),
        Row("Ramipril 5 mg", MedicineRowStatus.Warning),
        Row("Metformina 500 mg", MedicineRowStatus.Ok),
        Row("Pantoprazolo 20 mg", MedicineRowStatus.Suspended),
        Row("Amoxicillina 1 g", MedicineRowStatus.Inactive, active: false),
    ];

    [Fact]
    public void Inactive_rows_are_hidden_unless_asked_for()
    {
        MedicineListFilter.Visible(Rows, showInactive: false).Should().HaveCount(5);
        MedicineListFilter.Visible(Rows, showInactive: true).Should().HaveCount(6);
    }

    [Fact]
    public void Summary_counts_the_visible_rows_by_status()
    {
        var summary = MedicineListFilter.Summarize(MedicineListFilter.Visible(Rows, showInactive: false));

        summary.Should().Be(new MedicineListSummary(Empty: 1, Warning: 2, Suspended: 1, All: 5));
    }

    [Theory]
    [InlineData(MedicineListBucket.All, 5)]
    [InlineData(MedicineListBucket.Empty, 1)]
    [InlineData(MedicineListBucket.Warning, 2)]
    [InlineData(MedicineListBucket.Suspended, 1)]
    public void Bucket_keeps_the_rows_its_card_counts(MedicineListBucket bucket, int expected)
    {
        var visible = MedicineListFilter.Visible(Rows, showInactive: false);

        MedicineListFilter.Apply(visible, bucket, search: null).Should().HaveCount(expected);
    }

    [Theory]
    [InlineData("ramipril")]
    [InlineData("  RAMI ")]
    [InlineData("5 mg")]
    public void Search_matches_part_of_the_name_ignoring_case_and_spaces(string search)
    {
        var result = MedicineListFilter.Apply(Rows, MedicineListBucket.All, search);

        result.Select(r => r.Name).Should().Contain("Ramipril 5 mg");
    }

    [Fact]
    public void Search_and_bucket_combine_and_keep_load_order()
    {
        var result = MedicineListFilter.Apply(Rows, MedicineListBucket.Warning, "mg");

        result.Select(r => r.Name).Should().Equal("Cardioaspirin 100 mg", "Ramipril 5 mg");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_search_keeps_every_row(string? search)
        => MedicineListFilter.Apply(Rows, MedicineListBucket.All, search).Should().HaveCount(Rows.Count);
}
