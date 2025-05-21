#nullable enable
using System;
using System.Linq.Expressions;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Linq.Parsing.Methods.FullText;

internal enum FullTextSearchFunction
{
    to_tsquery,
    plainto_tsquery,
    phraseto_tsquery,
    websearch_to_tsquery,
    mt_ngram_tsvector
}

internal abstract class FullTextSearchMethodCallParser: IMethodCallParser
{
    private readonly string methodName;
    private readonly FullTextSearchFunction searchFunction;

    protected FullTextSearchMethodCallParser(string methodName, FullTextSearchFunction searchFunction)
    {
        this.methodName = methodName;
        this.searchFunction = searchFunction;
    }

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == methodName
               && expression.Method.DeclaringType == typeof(LinqExtensions);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if (expression.Arguments.Count < 2 || expression.Arguments[1].Value() == null)
        {
            throw new ArgumentException("Search Term needs to be provided", "searchTerm");
        }

        if (expression.Arguments[1].Type != typeof(string))
        {
            throw new ArgumentException("Search Term needs to be string", "searchTerm");
        }

        if (expression.Arguments.Count > 2 && expression.Arguments[2].Type != typeof(string))
        {
            throw new ArgumentException("Reg config needs to be string", "regConfig");
        }

        var searchTerm = (string)expression.Arguments[1].Value();

        var regConfig = expression.Arguments.Count > 2
            ? (expression.Arguments[2].Value() as string)!
            : FullTextIndexDefinition.DefaultRegConfig;

        return new FullTextWhereFragment(
            options.FindOrResolveDocumentType(memberCollection.ElementType) as DocumentMapping,
            searchFunction,
            searchTerm,
            regConfig);
    }
}
