using System;
using System.Linq.Expressions;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public class IsEmpty : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsEmpty)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var members = FindMembers.Determine(expression);

            var field = mapping.FieldFor(members);

            return new WhereFragment($"({field.SelectionLocator} is null or jsonb_array_length({field.SelectionLocator}) = 0)");
        }
    }
}