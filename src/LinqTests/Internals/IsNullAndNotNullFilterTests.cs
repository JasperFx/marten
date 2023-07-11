using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using NSubstitute;
using Shouldly;

namespace LinqTests.Internals;

public class IsNullAndNotNullFilterTests
{
    [Fact]
    public void reverse_is_null()
    {
        var filter = new IsNullFilter(Substitute.For<IQueryableMember>());
        filter.Reverse().ShouldBeOfType<IsNotNullFilter>()
            .Member.ShouldBe(filter.Member);
    }

    [Fact]
    public void reverse_not_null_is_null()
    {
        var filter = new IsNotNullFilter(Substitute.For<IQueryableMember>());
        filter.Reverse().ShouldBeOfType<IsNullFilter>()
            .Member.ShouldBe(filter.Member);
    }
}
