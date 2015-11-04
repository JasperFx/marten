using System.Linq.Expressions;
using Marten.Linq;
using Shouldly;

namespace Marten.Testing.Linq
{
    public class MartenExpressionParserTests
    {
        public void value_of_constant()
        {
            var constant = Expression.Constant("foo");

            MartenExpressionParser.Value(constant)
                .ShouldBe("foo");
        }
    }
}