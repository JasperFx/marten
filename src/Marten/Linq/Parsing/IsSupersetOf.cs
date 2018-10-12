using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema;
using NpgsqlTypes;

namespace Marten.Linq.Parsing
{
    public class IsSupersetOf : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            MethodInfo method = expression.Method;
            return method.Name == nameof(LinqExtensions.IsSupersetOf) && method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            MemberInfo[] members = FindMembers.Determine(expression);

            string locator = mapping.FieldFor(members).SqlLocator;
            object values = expression.Arguments.Last().Value();

            string json = serializer.ToJson(values);
            return new CustomizableWhereFragment($"{locator} @> ?", "?", Tuple.Create<object, NpgsqlDbType?>(json, NpgsqlDbType.Jsonb));
        }
    }
}