using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.Members;
using NpgsqlTypes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsSupersetOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var method = expression.Method;
        return isMartenLinqExtension(method) ||
               isISetMethod(method);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var member = memberCollection.MemberFor(expression.Object ?? expression.Arguments[0]);
        var locator = member.JSONBLocator;
        var values = expression.Arguments.Last().Value();

        var json = options.Serializer().ToJson(values);
        return new CustomizableWhereFragment($"{locator} @> ?", "?", new CommandParameter(json, NpgsqlDbType.Jsonb));
    }

    private static bool isMartenLinqExtension(MethodInfo method)
    {
        return method.Name == nameof(LinqExtensions.IsSupersetOf) && method.DeclaringType == typeof(LinqExtensions);
    }

    private static bool isISetMethod(MethodInfo method)
    {
        return method.Name == "IsSupersetOf" &&
               method.DeclaringType
                   .GetInterfaces()
                   .Where(i => i.IsGenericType)
                   .Any(i => i.GetGenericTypeDefinition() == typeof(ISet<>));
    }
}
