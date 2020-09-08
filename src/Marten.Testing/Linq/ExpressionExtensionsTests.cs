using System.Linq.Expressions;
using Marten.Linq;
using Marten.Linq.Parsing;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
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
}