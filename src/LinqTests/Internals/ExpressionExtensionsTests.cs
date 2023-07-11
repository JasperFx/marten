using System.Linq.Expressions;
using Marten.Linq.Parsing;
using Shouldly;

namespace LinqTests.Internals;

public class ExpressionExtensionsTests
{
    [Fact]
    public void value_of_constant()
    {
        var constant = Expression.Constant("foo");

        constant.Value()
            .ShouldBe("foo");
    }
}
