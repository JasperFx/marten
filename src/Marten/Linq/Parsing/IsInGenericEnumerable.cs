using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class IsInGenericEnumerable: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == MartenExpressionParser.CONTAINS &&
                   expression.Object.Type.IsGenericEnumerable() &&
                   !expression.Arguments.Single().IsValueExpression();
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = mapping.FieldFor(expression).TypedLocator;
            var values = expression.Object.Value();

            return new WhereFragment($"{locator} = ANY(?)", values);
        }
    }
}
