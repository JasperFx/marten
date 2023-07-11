using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsOneOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return (expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                || (expression.Method.Name == nameof(LinqExtensions.In) &&
                    !expression.Arguments.First().Type.IsGenericEnumerable()))
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var queryableMember = memberCollection.MemberFor(expression.Arguments[0]);
        var locator = queryableMember.TypedLocator;
        var values = expression.Arguments[1].ReduceToConstant().Value;

        if (queryableMember.MemberType.IsEnum)
        {
            return new EnumIsOneOfWhereFragment(values, options.Serializer().EnumStorage, locator);
        }

        return new WhereFragment($"{locator} = ANY(?)", values);
    }
}
