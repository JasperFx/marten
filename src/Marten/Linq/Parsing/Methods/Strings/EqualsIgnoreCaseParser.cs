#nullable enable
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.Strings;

internal class EqualsIgnoreCaseParser: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(StringExtensions.EqualsIgnoreCase)
               && expression.Method.DeclaringType == typeof(StringExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var locator = memberCollection.MemberFor(expression.Arguments[0]).RawLocator;
        var value = expression.Arguments.Last().Value();

        return new WhereFragment($"{locator} ~~* ?", value.As<string>());
    }
}
