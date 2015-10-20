using System.Linq.Expressions;
using FubuCore.Reflection;
using Marten.Linq;
using Marten.Testing.Documents;
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

        public void json_locator_of_a_simple_property()
        {
            var variable = Expression.Variable(typeof(User), "foo");
            var member = Expression.MakeMemberAccess(variable, ReflectionHelper.GetProperty<User>(x => x.FirstName));

            MartenExpressionParser.JsonLocator(member)
                .ShouldBe("data ->> 'FirstName'");
        }
    }
}