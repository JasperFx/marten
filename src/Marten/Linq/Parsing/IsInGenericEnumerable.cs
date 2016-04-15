using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class IsInGenericEnumerable : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == MartenExpressionParser.CONTAINS &&
                   expression.Object.Type.IsGenericEnumerable() &&
                   !expression.Arguments.Single().IsValueExpression();
        }

        public IWhereFragment Parse(IDocumentMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var finder = new FindMembers();
            finder.Visit(expression);

            var members = finder.Members;

            var locator = mapping.FieldFor(members).SqlLocator;
            var values = expression.Object.Value();

            return new WhereFragment($"{locator} = ANY(?)", values);
        }
    }
}