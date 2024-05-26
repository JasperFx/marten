#nullable enable
using System.Linq.Expressions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class StringIsNullOrEmpty: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(string.IsNullOrEmpty)
               && expression.Method.DeclaringType == typeof(string);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        // Thanks JT.
        if (expression.Arguments[0].TryToParseConstant(out var constant))
        {
            if (constant.Value == null || constant.Value.Equals(string.Empty)) return new LiteralTrue();

            return new LiteralFalse();
        }

        var locator = memberCollection.MemberFor(expression.Arguments[0]).RawLocator;

        return new WhereFragment($"({locator} IS NULL OR {locator} = '')");
    }
}
