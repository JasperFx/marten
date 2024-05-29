#nullable enable
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing.Methods.FullText;

public class NgramSearch: IMethodCallParser
{
    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(LinqExtensions.NgramSearch)
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var locator = memberCollection.MemberFor(expression.Arguments[0]).RawLocator;
        var values = expression.Arguments.Last().Value();

        return new WhereFragment(
            $"{options.DatabaseSchemaName}.mt_grams_vector({locator}) @@ {options.DatabaseSchemaName}.mt_grams_query(?)",
            values);
    }
}
