using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods
{
    internal class IsEmpty: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsEmpty)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var field = mapping.FieldFor(expression);

            return new WhereFragment($"({field.RawLocator} is null or jsonb_array_length({field.RawLocator}) = 0)");
        }
    }
}
