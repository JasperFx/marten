#nullable enable
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Linq.Members;
using Weasel.Postgresql;
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
        var member = memberCollection.MemberFor(expression.Arguments[0]);
        var value = expression.Arguments.Last().Value();

        return new StringEqualsIgnoreCaseFilter(member, new CommandParameter(value));
    }
}
