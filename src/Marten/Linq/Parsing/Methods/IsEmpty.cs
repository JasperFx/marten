#nullable enable
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
        var member = (ICollectionMember)memberCollection.MemberFor(expression);
        return member.IsEmpty;
    }
}
