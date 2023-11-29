using System.Linq.Expressions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
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
        var member = (ICollectionMember)memberCollection.MemberFor(expression);

        return new CollectionIsEmpty(member);

        // TODO -- memoize this off of ICollectionMember. Part of https://github.com/JasperFx/marten/issues/2703
        //return new WhereFragment($"({member.RawLocator} is null or jsonb_array_length({member.JSONBLocator}) = 0)");
    }
}
