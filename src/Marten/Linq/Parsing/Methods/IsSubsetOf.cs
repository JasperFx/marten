using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Fields;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsSubsetOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var method = expression.Method;
        return IsMartenLinqExtension(method) ||
               IsISetMethod(method);
    }

    public ISqlFragment Parse(IFieldMapping mapping, IReadOnlyStoreOptions options, MethodCallExpression expression)
    {
        var locator = mapping.FieldFor(expression).JSONBLocator;
        var values = expression.Arguments.Last().Value();

        var json = options.Serializer().ToJson(values);
        return new CustomizableWhereFragment($"{locator} <@ ?", "?", new CommandParameter(json, NpgsqlDbType.Jsonb));
    }

    private static bool IsMartenLinqExtension(MethodInfo method)
    {
        return method.Name == nameof(LinqExtensions.IsSubsetOf) && method.DeclaringType == typeof(LinqExtensions);
    }

    private static bool IsISetMethod(MethodInfo method)
    {
        return method.Name == "IsSubsetOf" &&
               method.DeclaringType
                   .GetInterfaces()
                   .Where(i => i.IsGenericType)
                   .Any(i => i.GetGenericTypeDefinition() == typeof(ISet<>));
    }
}
