using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Handlers
{
    public class EnumerableContains : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == MartenExpressionParser.CONTAINS &&
                   expression.Object.Type.IsGenericEnumerable();
        }

        public IWhereFragment Parse(IDocumentMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var value = expression.Arguments.Single().Value();
            return ContainmentWhereFragment.SimpleArrayContains(serializer, expression.Object,
                value);
        }
    }
}