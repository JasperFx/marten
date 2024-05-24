using System;
using System.Collections;
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

    private bool hasAny(object value)
    {
        if (value is IEnumerable<object> e) return e.Any();

        if (value is ICollection c) return c.Count > 0;

        return false;
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        Expression memberExpression = null;
        Expression body = null;

        ICollectionMember member = null;

        if (expression.Arguments.Count == 1)
        {
            if (expression.Arguments[0].TryToParseConstant(out var c))
            {
                if (c.Value == null) return new LiteralFalse();

                if (c.Value is Array a)
                {
                    return a.Length > 0 ? new LiteralTrue() : new LiteralFalse();
                }

                return hasAny(c.Value) ? new LiteralTrue() : new LiteralFalse();
            }

            // Where(filter).Any()
            else if (expression.Arguments[0] is MethodCallExpression method)
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

        if (type.Closes(typeof(ICollection<>))) return true;

        if (type.Closes(typeof(IDictionary<,>)))
        {
            return true;
        }

        if (type.Closes(typeof(Dictionary<,>.KeyCollection)))
        {
            return true;
        }

        if (type.Closes(typeof(Dictionary<,>.ValueCollection)))
        {
            return true;
        }

        return type.Closes(typeof(IReadOnlyList<>));
    }
}
