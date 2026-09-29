using FluentAssertions;
using MedReminder.Application.Reporting;
using Xunit;

namespace MedReminder.Application.Tests.Reporting;

public class TherapyCardPaginationTests
{
    [Fact]
    public void Empty_table_yields_one_page()
    {
        TherapyCardPagination.Paginate([], 100f)
            .Should().Equal(new TherapyCardPage(0, 0));
    }

    [Fact]
    public void Rows_that_fit_stay_on_one_page()
    {
        TherapyCardPagination.Paginate([30f, 30f, 40f], 100f)
            .Should().Equal(new TherapyCardPage(0, 3));
    }

    [Fact]
    public void Long_list_spans_several_pages_without_splitting_rows()
    {
        var heights = Enumerable.Repeat(30f, 10).ToList();

        TherapyCardPagination.Paginate(heights, 100f).Should().Equal(
            new TherapyCardPage(0, 3),
            new TherapyCardPage(3, 3),
            new TherapyCardPage(6, 3),
            new TherapyCardPage(9, 1));
    }

    [Fact]
    public void Row_taller_than_a_page_gets_a_page_of_its_own()
    {
        TherapyCardPagination.Paginate([20f, 250f, 20f], 100f).Should().Equal(
            new TherapyCardPage(0, 1),
            new TherapyCardPage(1, 1),
            new TherapyCardPage(2, 1));
    }

    [Fact]
    public void Non_positive_available_height_is_rejected()
    {
        var act = () => TherapyCardPagination.Paginate([10f], 0f);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
