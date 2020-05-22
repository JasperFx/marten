using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class EqualsIgnoreCaseParser: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(StringExtensions.EqualsIgnoreCase)
                   && expression.Method.DeclaringType == typeof(StringExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = mapping.FieldFor(expression).RawLocator;
            var value = expression.Arguments.Last().Value();

            return new WhereFragment($"{locator} ~~* ?", value.As<string>());
        }
    }
}
