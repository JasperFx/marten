using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Parsing.Methods
{
    internal class IsInGenericEnumerable: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == LinqConstants.CONTAINS &&
                   expression.Object.Type.IsGenericEnumerable() &&
                   !expression.Arguments.Single().IsValueExpression();
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = mapping.FieldFor(expression).TypedLocator;
            var values = expression.Object.Value();

            return new WhereFragment($"{locator} = ANY(?)", values);
        }
    }
}
