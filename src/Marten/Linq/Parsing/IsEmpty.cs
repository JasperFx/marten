using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class IsEmpty: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsEmpty)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var field = mapping.FieldFor(expression);

            return new WhereFragment($"({field.RawLocator} is null or jsonb_array_length({field.RawLocator}) = 0)");
        }
    }
}
