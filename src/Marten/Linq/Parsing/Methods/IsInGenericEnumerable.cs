using System;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

[Obsolete("Eliminate with https://github.com/JasperFx/marten/issues/2703")]
internal class IsInGenericEnumerable: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == LinqConstants.CONTAINS &&
               (expression.Object ?? expression.Arguments[0]).Type.IsGenericEnumerable() &&
               !expression.Arguments.Last().IsValueExpression();
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var locator = memberCollection.MemberFor(expression.Arguments.Single()).TypedLocator;
        var values = expression.Object.ReduceToConstant().Value;

        return new WhereFragment($"{locator} = ANY(?)", values);
    }
}
