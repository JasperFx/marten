using System.Linq.Expressions;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class IsEmpty: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(LinqExtensions.IsEmpty)
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var field = memberCollection.MemberFor(expression);

        // TODO -- memoize this off of ICollectionMember. Part of https://github.com/JasperFx/marten/issues/2703
        return new WhereFragment($"({field.RawLocator} is null or jsonb_array_length({field.JSONBLocator}) = 0)");
    }
}
