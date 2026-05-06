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
        // C# 14 with <Nullable>enable</Nullable> can wrap captured-closure
        // string[] locals with an extra Convert() before the implicit
        // string[] -> ReadOnlySpan<string> conversion, e.g.
        //     op_Implicit(Convert(closureField, String[])).Contains(s.Name)
        // Strip both forms (and any stacked combination) so the receiver can
        // still be reduced to a constant.
        while (true)
        {
            if (expression is MethodCallExpression
                {
                    Method.Name: "op_Implicit" or "op_Explicit",
                    Arguments.Count: > 0
                } methodCall)
            {
                expression = methodCall.Arguments[0];
                continue;
            }

            if (expression is UnaryExpression
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
                } unary)
            {
                expression = unary.Operand;
                continue;
            }

            return expression;
        }
    }
}
