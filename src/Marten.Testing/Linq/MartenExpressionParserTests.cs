using System.Linq.Expressions;
using Marten.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class MartenExpressionParserTests
    {
        [Fact]
        public void value_of_constant()
        {
            var constant = Expression.Constant("foo");

            MartenExpressionParser.Value(constant)
                .ShouldBe("foo");
        }
    }
}