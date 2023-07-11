using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsNotOneOf: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return (expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                || expression.Method.Name == nameof(LinqExtensions.In))
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var queryableMember = memberCollection.MemberFor(expression);
        var locator = queryableMember.TypedLocator;
        var values = expression.Arguments.Last().Value();

        if (queryableMember.MemberType.IsEnum)
        {
            return new EnumIsNotOneOfWhereFragment(values, options.Serializer().EnumStorage, locator);
        }

        return new WhereFragment($"NOT({locator} = ANY(?))", values);
    }
}
