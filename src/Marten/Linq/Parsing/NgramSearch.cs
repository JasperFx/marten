using System.Linq;
using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class NgramSearch : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.NgramSearch)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var members = FindMembers.Determine(expression);

            var locator = mapping.FieldFor(members).SqlLocator;
            var values = expression.Arguments.Last().Value();

            return new WhereFragment($"mt_grams_vector({locator}) @@ mt_grams_query(?)", values);
        }
    }
}
