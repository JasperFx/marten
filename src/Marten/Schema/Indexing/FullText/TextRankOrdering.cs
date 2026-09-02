#nullable enable
using System;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Schema.Indexing.FullText;

/// <summary>
///     Which <c>tsquery</c> function a text search — and a rank over it — is built with.
/// </summary>
/// <remarks>
///     Deliberately explicit with no default. Ranking with a different tsquery function than the
///     <c>Where</c> used is always a mistake, and inferring it from a sibling <c>Where</c> clause is
///     fragile. A friendly default here would be occasionally wrong and silently so.
/// </remarks>
public enum TextSearchFunction
{
    /// <summary><c>plainto_tsquery</c> — every word required, no operators.</summary>
    Plain,

    /// <summary><c>phraseto_tsquery</c> — words required, in order and adjacent.</summary>
    Phrase,

    /// <summary><c>websearch_to_tsquery</c> — web-style syntax: quotes, <c>or</c>, <c>-</c>.</summary>
    WebStyle,

    /// <summary><c>to_tsquery</c> — raw tsquery syntax, operators and all.</summary>
    Raw
}

/// <summary>
///     A <c>ts_rank</c> relevance ordering (#5298).
/// </summary>
/// <remarks>
///     <para>
///         The vector is resolved through <see cref="FullTextIndexResolver" />, the same path the
///         <c>@@</c> filter uses. That is the whole design constraint: a rank computed over a different
///         vector than the filter matched on is <em>silently wrong</em> rather than merely slow — rows
///         come back in an order that looks plausible and means nothing. Resolving both through one place
///         is what makes them incapable of disagreeing.
///     </para>
///     <para>
///         The search term is bound as a parameter. The ngram precedent
///         (<c>Ordering.BuildNgramRankExpression</c>) inlines its term with <c>Replace("'", "''")</c>,
///         which is correct under <c>standard_conforming_strings</c> but exists only because the ordering
///         pipeline used to be a list of strings with nowhere to put a parameter. Now that it holds
///         fragments, there is no reason to interpolate a user value into SQL — a class of bug this repo
///         has shipped advisories for more than once.
///     </para>
/// </remarks>
internal class TextRankOrdering
{
    public TextRankOrdering(string searchTerm, TextSearchFunction function, string regConfig,
        DocumentMapping? mapping)
    {
        SearchTerm = searchTerm;
        Function = function;
        RegConfig = regConfig;
        Mapping = mapping;
    }

    public string SearchTerm { get; }
    public TextSearchFunction Function { get; }
    public string RegConfig { get; }
    public DocumentMapping? Mapping { get; }

    public ISqlFragment Build(IQueryableMemberCollection collection, OrderingDirection direction)
    {
        var vector = FullTextIndexResolver.ResolveVector(Mapping, RegConfig);
        return new TextRankFragment(vector, RegConfig, Function, SearchTerm, direction);
    }

    internal static string ToSqlFunction(TextSearchFunction function) =>
        function switch
        {
            TextSearchFunction.Plain => "plainto_tsquery",
            TextSearchFunction.Phrase => "phraseto_tsquery",
            TextSearchFunction.WebStyle => "websearch_to_tsquery",
            TextSearchFunction.Raw => "to_tsquery",
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown text search function")
        };
}

internal class TextRankFragment: ISqlFragment
{
    private readonly OrderingDirection _direction;
    private readonly TextSearchFunction _function;
    private readonly string _regConfig;
    private readonly string _searchTerm;
    private readonly string _vector;

    public TextRankFragment(string vector, string regConfig, TextSearchFunction function, string searchTerm,
        OrderingDirection direction)
    {
        _vector = vector;
        _regConfig = regConfig;
        _function = function;
        _searchTerm = searchTerm;
        _direction = direction;
    }

    public void Apply(ICommandBuilder builder)
    {
        // regConfig is interpolated rather than parameterized, matching FullTextWhereFragment: it ruins
        // the query plan as a parameter, and it is validated against a strict identifier pattern before
        // it reaches here. The search TERM is the user value, and it is bound.
        var direction = _direction == OrderingDirection.Desc ? " desc" : " asc";
        var sql =
            $"ts_rank({_vector}, {TextRankOrdering.ToSqlFunction(_function)}('{_regConfig}'::regconfig, ?)){direction}";

        builder.AppendWithParameters(sql)[0].Value = _searchTerm;
    }
}
