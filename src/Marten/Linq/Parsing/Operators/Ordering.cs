#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.Members.Unions;
using Marten.Linq.Members.ValueCollections;
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

        // #5957: a key computed from the document's members, OrderBy(x => -x.Number). MemberFinder walked past the
        // computation to the member underneath it and silently ordered by x.Number instead.
        // #5968: and the same for a key over the child documents a SelectMany() flattens
        if (_literal.IsEmpty() && MemberName.IsEmpty() &&
            collection is DocumentQueryableMemberCollection or ChildCollectionMember &&
            ComputedKeyBody(Expression) is { } computed)
        {
            return buildComputedKey(collection, computed);
        }

        return new LiteralOrdering(BuildExpression(collection));
    }

    /// <summary>
    ///     #5957: the serializer of the store, which decides how a computed key's enums and types are written.
    ///     Captured when the ordering is parsed.
    /// </summary>
    internal ISerializer? Serializer { get; init; }

    private ISqlFragment buildComputedKey(IQueryableMemberCollection collection, Expression body)
    {
        if (Serializer == null)
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate the OrderBy() key '{body}': no serializer was captured for it");
        }

        if (!FlattenedRowProjection.TryTranslateScalar(body, collection, Serializer, out var fragment, out var reason))
        {
            throw new BadLinqExpressionException(
                $"Marten cannot translate the OrderBy() key '{body}' into SQL that orders the way .NET does: {reason}. Order by a member of the document, or by a key Marten can compute: member access, arithmetic, widening casts, string concatenation, ToUpper(), ToLower(), Length, or ToString() of an integer or a Guid. Or sort in memory after the query.");
        }

        if (CasingRule == CasingRule.CaseInsensitive && body.Type == typeof(string))
        {
            fragment = new ComputedKeyFragment(fragment, "lower(", ")");
        }

        return new ComputedKeyFragment(fragment, "", Direction == OrderingDirection.Desc ? " desc" : "");
    }

    /// <summary>
    ///     #5957: the body of a key selector that computes a value from the document, such as
    ///     <c>x => -x.Number</c>, <c>x => x.Number % 3</c> or <c>x => x.Code + x.Name</c>, rather than reading a
    ///     member. Null for a plain member chain, which keeps resolving exactly as it always has -- including
    ///     <c>x.Name.Length</c>, which resolves to <c>length()</c> as a member since #5941.
    /// </summary>
    internal static Expression? ComputedKeyBody(Expression? expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
        {
            expression = quote.Operand;
        }

        // Only a key selector computes anything. OrderBy("Name") names its member as a string.
        if (expression is not LambdaExpression lambda)
        {
            return null;
        }

        expression = lambda.Body;

        // A key selector typed as object boxes its value
        var body = expression is UnaryExpression { NodeType: ExpressionType.Convert } box && box.Type == typeof(object)
            ? box.Operand
            : expression;

        return isPlainKey(body) ? null : body;
    }

    /// <summary>
    ///     Is this a chain the member lookup resolves exactly: members (a string's Length among them), an element by
    ///     position or a dictionary key, an array length or Count(), a case conversion of a string, and conversions
    ///     that do not change the value?
    /// </summary>
    private static bool isPlainKey(Expression? node)
    {
        while (true)
        {
            switch (node)
            {
                case null:
                case ParameterExpression:
                    return true;

                case MemberExpression member:
                    node = member.Expression;
                    continue;

                case UnaryExpression { NodeType: ExpressionType.ArrayLength } length:
                    node = length.Operand;
                    continue;

                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } conversion
                    when UnionMembers.CaseCastFor(conversion) != null || conversion.Type == typeof(object) ||
                         (Nullable.GetUnderlyingType(conversion.Type) ?? conversion.Type) ==
                         (Nullable.GetUnderlyingType(conversion.Operand.Type) ?? conversion.Operand.Type):
                    node = conversion.Operand;
                    continue;

                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } index:
                    node = index.Left;
                    continue;

                case MethodCallExpression call when call.IsElementIndexer():
                    node = call.Object ?? call.Arguments[0];
                    continue;

                case MethodCallExpression { Method.Name: "get_Item", Object: not null } call
                    when call.Method.DeclaringType.Closes(typeof(IDictionary<,>)):
                    node = call.Object;
                    continue;

                case MethodCallExpression { Object: not null, Arguments.Count: 0 } call
                    when call.Object.Type == typeof(string) && call.Method.Name is nameof(string.ToLower)
                        or nameof(string.ToUpper) or nameof(string.ToLowerInvariant) or nameof(string.ToUpperInvariant):
                    node = call.Object;
                    continue;

                case MethodCallExpression { Method.Name: nameof(Enumerable.Count), Object: null, Arguments.Count: 1 } call
                    when call.Method.DeclaringType == typeof(Enumerable):
                    node = call.Arguments[0];
                    continue;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    ///     #5957: a translated key, wrapped in SQL text
    /// </summary>
    private sealed class ComputedKeyFragment: ISqlFragment
    {
        private readonly ISqlFragment _inner;
        private readonly string _prefix;
        private readonly string _suffix;

        public ComputedKeyFragment(ISqlFragment inner, string prefix, string suffix)
        {
            _inner = inner;
            _prefix = prefix;
            _suffix = suffix;
        }

        public void Apply(ICommandBuilder builder)
        {
            builder.Append(_prefix);
            _inner.Apply(builder);
            builder.Append(_suffix);
        }
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

        // #5949: an element read by position, x.Values[1], x.Grid[1][0] or x.Children[0].Name. MemberFinder
        // treats the index as invalid, so it resolves the way the same chain does in a Where() comparison.
        if (expression.HasElementIndex())
        {
            return collection.MemberForIndexedChain(expression);
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
