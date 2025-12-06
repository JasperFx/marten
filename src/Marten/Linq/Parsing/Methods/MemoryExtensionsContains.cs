#nullable enable
using System;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods;

internal class MemoryExtensionsContains: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == LinqConstants.CONTAINS
               && expression.Method.DeclaringType == typeof(MemoryExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        // MemoryExtensions.Contains is always an extension method
        // Arguments[0] is the span/collection (the 'this' parameter)
        // Arguments[1] is the value to find

        // Unwrap any implicit conversions that convert to Span/ReadOnlySpan
        var collectionExpression = UnwrapConversions(expression.Arguments[0]);

        if (collectionExpression.TryToParseConstant(out var constant))
        {
            // This is the constant.Contains(value) pattern
            var collectionMember = memberCollection.MemberFor(expression.Arguments[1]);
            return new IsOneOfFilter(collectionMember, new CommandParameter(constant.Value));
        }

        if (memberCollection.MemberFor(collectionExpression) is not ICollectionMember collection)
        {
            throw new BadLinqExpressionException(
                $"Marten is not (yet) able to parse '{expression}' as part of a Contains() query for this member");
        }

        return collection.ParseWhereForContains(expression, options);
    }

    private static Expression UnwrapConversions(Expression expression)
    {
        // Unwrap op_Implicit method calls
        if (expression is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: > 0 } methodCall)
        {
            return methodCall.Arguments[0];
        }
        return expression;
    }
}
