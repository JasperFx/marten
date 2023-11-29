using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class AnySubQueryParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        var member = expression.Object ?? expression.Arguments[0];

        return expression.Method.Name == LinqConstants.ANY &&
               typeMatches(member.Type);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        Expression memberExpression = null;
        Expression body = null;

        ICollectionMember member = null;

        if (expression.Arguments.Count == 1)
        {
            // Where(filter).Any()
            if (expression.Arguments[0] is MethodCallExpression method)
            {
                // Where(filter).Any()
                memberExpression = method.Arguments[0];
                body = method.Arguments.Last();
            }
            else
            {
                // Any(filter)
                memberExpression = expression.Object ?? expression.Arguments[0];
                member = (ICollectionMember)memberCollection.MemberFor(memberExpression);
                return member.NotEmpty;
            }
        }


        memberExpression ??= expression.Object ?? expression.Arguments[0];

        if (memberExpression.TryToParseConstant(out var constant))
        {
            throw new BadLinqExpressionException($"Marten cannot parse this expression: '{expression}'");
        }

        body ??= expression.Arguments.Last();

        member = (ICollectionMember)memberCollection.MemberFor(memberExpression);
        if (body is LambdaExpression l)
        {
            body = l.Body;
        }

        return member.ParseWhereForAny(body, options);
    }

    private static bool typeMatches(Type type)
    {
        if (type.IsGenericEnumerable())
        {
            return true;
        }

        return type.Closes(typeof(IReadOnlyList<>));
    }
}
