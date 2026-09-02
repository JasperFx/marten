#nullable enable
using System.Collections.Generic;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.SqlGeneration;
using Marten.Schema.Indexing.FullText;
using Weasel.Postgresql.SqlGeneration;

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

    /// <summary>
    /// For ts_rank ordering (#5298): everything needed to render a relevance ordering that resolves the
    /// same tsvector the Where clause matched on, and binds its search term as a parameter.
    /// </summary>
    internal TextRankOrdering? TextRank { get; init; }

    /// <summary>
    ///     Build this ordering as a SQL fragment, so that an ordering carrying a user value can bind it as
    ///     a parameter rather than interpolating it (#5298).
    /// </summary>
    /// <remarks>
    ///     Every other ordering is a member locator or a caller-supplied literal, neither of which
    ///     contains user input, so those keep going through <see cref="BuildExpression" /> and are wrapped
    ///     as a <see cref="LiteralOrdering" />.
    /// </remarks>
    public ISqlFragment BuildFragment(IQueryableMemberCollection collection)
    {
        if (TextRank != null)
        {
            return TextRank.Build(collection, Direction);
        }

        return new LiteralOrdering(BuildExpression(collection));
    }

    public string BuildExpression(IQueryableMemberCollection collection)
    {
        if (NgramRankSearchTerm != null && NgramRankMemberExpression != null)
        {
            return BuildNgramRankExpression(collection);
        }

        if (_literal.IsNotEmpty()) return _literal;

        var member = MemberName.IsNotEmpty()
            ? collection.MemberFor(MemberName)
            : MemberForExpression(collection);

        return member.BuildOrderingExpression(this, CasingRule);
    }

    /// <summary>
    /// Resolve the member being ordered by. A dictionary indexer (<c>x.Attributes["key"]</c>)
    /// is a get_Item method call rather than a member access, so MemberFinder drops it and the
    /// generic lookup lands on the dictionary itself — which orders by the whole JSON object
    /// and silently ignores the key. Resolve it the same way the Where() path does. See #5063.
    /// </summary>
    private IQueryableMember MemberForExpression(IQueryableMemberCollection collection)
    {
        // The operator hands us the raw OrderBy argument, i.e. a quoted lambda. Unwrap to the
        // body, then past any boxing conversion, to see whether it is an indexer access.
        var expression = Expression;
        while (true)
        {
            switch (expression)
            {
                case UnaryExpression { NodeType: ExpressionType.Quote or ExpressionType.Convert } unary:
                    expression = unary.Operand;
                    continue;
                case LambdaExpression lambda:
                    expression = lambda.Body;
                    continue;
            }

            break;
        }

        if (expression is MethodCallExpression { Method.Name: "get_Item" } call
            && call.Object != null
            && call.Arguments.Count == 1
            && call.Method.DeclaringType.Closes(typeof(IDictionary<,>))
            && collection.MemberFor(call.Object) is IDictionaryMember dictionary)
        {
            return dictionary.MemberForKey(call.Arguments[0].Value());
        }

        // Deliberately the original expression, not the unwrapped one: the unwrapping above
        // exists only to spot an indexer, and BadLinqExpressionException quotes whatever it
        // is handed. Passing the body would drop the "x => " from the message.
        return collection.MemberFor(Expression, "Invalid OrderBy() expression");
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
