#nullable enable
using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Npgsql;
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
        var member = memberCollection.MemberFor(expression.Arguments[0]);

        // check if NGramSearch is called on the doc itself and disallow the operation
        if (memberCollection.ElementType == member.MemberType)
        {
            throw new InvalidOperationException(
                $"{nameof(NgramSearch)} extension method should target a property and not the document itself.");
        }

        // check if NGramSearch is performed on a computed property value and disallow the operation
        if (expression.Arguments[0] is not MemberExpression)
        {
            throw new InvalidOperationException(
                $"{nameof(NgramSearch)} extension method cannot be applied to computed values. It must be applied directly on a string property/field.");
        }

        // check if NGramSearch is performed on a non-string property and disallow the operation
        if (member.MemberType != typeof(string))
        {
            throw new InvalidOperationException(
                $"{nameof(NgramSearch)} extension method cannot be applied on non-string property/field.");
        }

        var values = expression.Arguments.Last().Value();
        var useUnaccent = options.Advanced.UseNGramSearchWithUnaccent.ToString().ToUpperInvariant();

        return new WhereFragment(
            $"{options.DatabaseSchemaName}.mt_grams_vector({member.RawLocator},{useUnaccent}) @@ {options.DatabaseSchemaName}.mt_grams_query(?,{useUnaccent})",
            values);
    }
}
