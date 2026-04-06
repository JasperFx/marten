#nullable enable
using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Linq.Members;

namespace Marten.Linq.Parsing.Operators;

public class Ordering
{
    public string? MemberName { get; set; }
    private readonly string? _literal;

    public Ordering(Expression expression, OrderingDirection direction)
    {
        Expression = expression;
        Direction = direction;
    }

    public Ordering(string literal)
    {
        _literal = literal;
    }

    public Ordering(string memberName, OrderingDirection direction)
    {
        MemberName = memberName;
        Direction = direction;
    }

    public string? Literal => _literal;

    public Expression Expression { get; }

    public OrderingDirection Direction { get; set; }

    public CasingRule CasingRule { get; set; } = CasingRule.CaseSensitive;

    /// <summary>
    /// Refers to whether or not this ordering is transformed such that it cannot
    /// be combined with a Distinct(Select()) usage
    /// </summary>
    public bool IsTransformed { get; set; }

    /// <summary>
    /// For NgramRank ordering: the search term to rank against.
    /// </summary>
    internal string? NgramRankSearchTerm { get; init; }

    /// <summary>
    /// For NgramRank ordering: the member expression to resolve at compilation time.
    /// </summary>
    internal Expression? NgramRankMemberExpression { get; init; }

    /// <summary>
    /// For NgramRank ordering: the store options for schema name and unaccent config.
    /// </summary>
    internal StoreOptions? NgramRankOptions { get; init; }

    public string BuildExpression(IQueryableMemberCollection collection)
    {
        if (NgramRankSearchTerm != null && NgramRankMemberExpression != null)
        {
            return BuildNgramRankExpression(collection);
        }

        if (_literal.IsNotEmpty()) return _literal;

        var member = MemberName.IsNotEmpty()
            ? collection.MemberFor(MemberName)
            : collection.MemberFor(Expression, "Invalid OrderBy() expression");

        return member.BuildOrderingExpression(this, CasingRule);
    }

    private string BuildNgramRankExpression(IQueryableMemberCollection collection)
    {
        var member = collection.MemberFor(NgramRankMemberExpression!, "Invalid OrderByNgramRank() member expression");
        var schemaName = NgramRankOptions!.DatabaseSchemaName;
        var useUnaccent = NgramRankOptions.Advanced.UseNGramSearchWithUnaccent.ToString().ToUpperInvariant();
        var escapedTerm = NgramRankSearchTerm!.Replace("'", "''");

        return $"ts_rank({schemaName}.mt_grams_vector({member.RawLocator},{useUnaccent}), " +
               $"{schemaName}.mt_grams_query('{escapedTerm}',{useUnaccent})) desc";
    }
}
