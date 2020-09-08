using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using NpgsqlTypes;

namespace Marten.Linq.Parsing.Methods
{
    internal class IsSupersetOf: IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            MethodInfo method = expression.Method;
            return IsMartenLinqExtension(method) ||
                   IsISetMethod(method);
        }

        private static bool IsMartenLinqExtension(MethodInfo method)
        {
            return method.Name == nameof(LinqExtensions.IsSupersetOf) && method.DeclaringType == typeof(LinqExtensions);
        }

        private static bool IsISetMethod(MethodInfo method)
        {
            return method.Name == "IsSupersetOf" &&
                   method.DeclaringType
                       .GetInterfaces()
                       .Where(i => i.IsGenericType)
                       .Any(i => i.GetGenericTypeDefinition() == typeof(ISet<>));
        }

        public ISqlFragment Parse(IFieldMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = mapping.FieldFor(expression).JSONBLocator;
            var values = expression.Arguments.Last().Value();

            var json = serializer.ToJson(values);
            return new CustomizableWhereFragment($"{locator} @> ?", "?", new CommandParameter(json, NpgsqlDbType.Jsonb));
        }
    }
}
