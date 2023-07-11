using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class EnumerableContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];

        return expression.Method.Name == LinqConstants.CONTAINS && (member.Type.IsEnumerable() ||
                                                                    member.Type.IsGenericEnumerable() ||
                                                                    member.Type.Closes(typeof(IDictionary<,>)));
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if ((expression.Object ?? expression.Arguments[0]).TryToParseConstant(out var constant))
        {
            // This is the value.Contains() pattern
            var collectionMember = memberCollection.MemberFor(expression.Arguments.Last());

            return new WhereFragment($"{collectionMember.TypedLocator} = ANY(?)", constant.Value);
        }

        var member = memberCollection.MemberFor(expression.Object ?? expression.Arguments[0]);
        var collection = (ICollectionMember)member;

        return collection.ParseWhereForContains(expression, options);
    }
}
