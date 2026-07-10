#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
///     Implemented by filters whose jsonpath rendering has to bake the comparison
///     value into the jsonpath string literal (like_regex patterns cannot be
///     supplied through the vars parameter). Compiled queries can never re-bind
///     such a value, so the jsonpath machinery fails loudly instead of silently
///     reusing a stale value.
/// </summary>
internal interface IInlinedJsonPathValueFilter
{
    bool JsonPathValueIsInlined { get; }
}

/// <summary>
///     jsonpath string operations (starts with / like_regex) evaluate to UNKNOWN on
///     null or missing members, so a bare !(...) negation silently skips those
///     elements — wrong for All(predicate), where a null member must FAIL the
///     predicate like it does in LINQ-to-objects. Implementors render their own
///     null-guarded negation instead.
/// </summary>
internal interface INegationGuardedJsonPathFilter
{
    void BuildNegatedJsonPathFilter(ICommandBuilder builder, Dictionary<string, object> parameters);
}

/// <summary>
///     Translates a child collection Any(predicate) that cannot be reduced to JSONB
///     containment (@>) into a single jsonb_path_exists() predicate instead of the
///     much more expensive "explode the collection into a CTE and correlate on ctid"
///     sub-query strategy. Values are passed through the jsonpath vars parameter so
///     the SQL stays parameterized. Also serves All(predicate) as
///     NOT jsonb_path_exists('$.coll[*] ? (!(predicate))').
/// </summary>
internal class JsonPathExistsFilter: ISqlFragment, ICollectionAwareFilter, ICompiledQueryAwareFilter,
    IReversibleWhereFragment
{
    private readonly bool _negatePredicate;
    private readonly ISqlFragment _predicate;
    private readonly ISerializer _serializer;
    private string _collectionPath;
    private string _documentColumn;
    private Dictionary<string, object>? _dict;
    private List<DictionaryValueUsage>? _usages;

    private JsonPathExistsFilter(ICollectionMember collection, ISqlFragment predicate, ISerializer serializer,
        bool negatePredicate = false)
    {
        CollectionMember = collection;
        _predicate = predicate;
        _serializer = serializer;
        _negatePredicate = negatePredicate;
        IsNot = negatePredicate;

        // ChildCollectionMember's segment is "Member[*]", but scalar value collections
        // write a bare "Member" — normalize so the filter always iterates elements
        // explicitly instead of leaning on lax-mode array unwrapping
        _collectionPath = collection.WriteJsonPath();
        if (!_collectionPath.EndsWith("[*]"))
        {
            _collectionPath += "[*]";
        }

        _documentColumn = collection.Ancestors[0].RawLocator;
    }

    public bool IsNot { get; private set; }

    public ICollectionMember CollectionMember { get; }

    public static bool TryBuild(ISqlFragment fragment, ICollectionMember collectionMember, ISerializer serializer,
        [NotNullWhen(true)] out JsonPathExistsFilter? filter)
    {
        filter = default;
        if (!canRender(fragment))
        {
            return false;
        }

        filter = new JsonPathExistsFilter(collectionMember, fragment, serializer);
        return true;
    }

    /// <summary>
    ///     All(predicate) == NOT EXISTS an element failing the predicate. Vacuously
    ///     true on empty collections, matching LINQ-to-objects semantics.
    /// </summary>
    public static bool TryBuildForAll(ISqlFragment fragment, ICollectionMember collectionMember,
        ISerializer serializer, [NotNullWhen(true)] out JsonPathExistsFilter? filter)
    {
        filter = default;
        if (!canRender(fragment))
        {
            return false;
        }

        filter = new JsonPathExistsFilter(collectionMember, fragment, serializer, negatePredicate: true);
        return true;
    }

    private static bool canRender(ISqlFragment fragment)
    {
        switch (fragment)
        {
            case ICollectionAware aware when aware.CanBeJsonPathFilter():
                return aware.Values().All(x => isSafeValue(x.Value));

            case CompoundWhereFragment compound:
                return compound.Children.Any() && compound.Children.All(canRender);

            default:
                return false;
        }
    }

    private static bool isSafeValue(object? value)
    {
        if (value == null)
        {
            return false;
        }

        // Serialized date representations do not reliably compare with the
        // jsonpath operators, so those predicates stay on the sub-query strategy
        if (value.GetType().IsDateTime() || value is DateTimeOffset)
        {
            return false;
        }

        return true;
    }

    public void Apply(ICommandBuilder builder)
    {
        if (IsNot)
        {
            builder.Append("NOT(");
        }

        builder.Append("jsonb_path_exists(");
        builder.Append(_documentColumn);
        builder.Append(", '$.");
        builder.Append(_collectionPath);
        builder.Append(" ? (");

        _dict = new Dictionary<string, object>();
        if (_negatePredicate)
        {
            writeNegatedPredicate(builder, _predicate);
        }
        else
        {
            writePredicate(builder, _predicate);
        }

        builder.Append(")'");

        if (_dict.Count > 0)
        {
            builder.Append(", ");
            builder.AppendParameter(_serializer.ToCleanJson(_dict), NpgsqlDbType.Jsonb);
            ParameterName = builder.LastParameterName;
        }

        builder.Append(")");

        if (IsNot)
        {
            builder.Append(")");
        }
    }

    private void writePredicate(ICommandBuilder builder, ISqlFragment fragment)
    {
        switch (fragment)
        {
            case CompoundWhereFragment compound:
                var separator = compound.Separator.ContainsIgnoreCase("and") ? " && " : " || ";
                builder.Append("(");
                var first = true;
                foreach (var child in compound.Children)
                {
                    if (!first)
                    {
                        builder.Append(separator);
                    }

                    writePredicate(builder, child);
                    first = false;
                }

                builder.Append(")");
                break;

            case ICollectionAware aware:
                aware.BuildJsonPathFilter(builder, _dict!);
                break;

            default:
                throw new InvalidOperationException(
                    $"Fragment {fragment} cannot be rendered as a jsonpath predicate. This is a Marten bug — TryBuild should have rejected it.");
        }
    }

    /// <summary>
    ///     Renders NOT(predicate) with the negation distributed De Morgan style so that
    ///     each leaf can apply its own null guard. Kleene 3-valued logic makes the
    ///     distribution itself equivalence-preserving; the per-leaf guards are what
    ///     turn UNKNOWN (null/missing member under a string operation) into a definite
    ///     "fails the predicate".
    /// </summary>
    private void writeNegatedPredicate(ICommandBuilder builder, ISqlFragment fragment)
    {
        switch (fragment)
        {
            case CompoundWhereFragment compound:
                // !(a && b) == !a || !b ; !(a || b) == !a && !b
                var separator = compound.Separator.ContainsIgnoreCase("and") ? " || " : " && ";
                builder.Append("(");
                var first = true;
                foreach (var child in compound.Children)
                {
                    if (!first)
                    {
                        builder.Append(separator);
                    }

                    writeNegatedPredicate(builder, child);
                    first = false;
                }

                builder.Append(")");
                break;

            case INegationGuardedJsonPathFilter guarded:
                guarded.BuildNegatedJsonPathFilter(builder, _dict!);
                break;

            case ICollectionAware aware:
                builder.Append("!(");
                aware.BuildJsonPathFilter(builder, _dict!);
                builder.Append(")");
                break;

            default:
                throw new InvalidOperationException(
                    $"Fragment {fragment} cannot be rendered as a jsonpath predicate. This is a Marten bug — TryBuildForAll should have rejected it.");
        }
    }

    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
        // A NEGATED filter must not flatten: NOT(exists($.M[*].B[*] ? pred)) asserts
        // over every middle's bottoms, which is stronger than Any(m => !m.B.Any(pred))
        // or Any(m => m.B.All(pred)). Those nest through the explode strategy instead,
        // where this fragment renders correctly against the exploded element.
        if (IsNot || _negatePredicate)
        {
            if (ancestorCollection is IExistsElementSource { ExplodedElementSource: not null } source)
            {
                return new ExistsCollectionFilter(ancestorCollection, this, source.ExplodedElementSource!);
            }

            return new SubQueryFilter(ancestorCollection, this);
        }

        // An existence test over a doubly-nested collection flattens cleanly:
        // exists(middle.bottoms[*] ? pred) == '$.Middles[*].Bottoms[*] ? pred'
        _collectionPath = ancestorCollection.WriteJsonPath() + "." + _collectionPath;
        _documentColumn = ancestorCollection.Ancestors[0].RawLocator;
        return this;
    }

    public ISqlFragment Reverse()
    {
        IsNot = !IsNot;
        return this;
    }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        _usages ??= collectLeaves(_predicate).SelectMany(x => x.Values()).ToList();

        var usage = _usages.FirstOrDefault(x => x.Value.Equals(value));
        if (usage != null)
        {
            // A compiled query member value that lands in a leaf whose rendering bakes
            // the value into the jsonpath literal (like_regex) can never be re-bound —
            // fail loudly at plan time instead of silently reusing the stale value
            foreach (var leaf in collectLeaves(_predicate))
            {
                if (leaf is IInlinedJsonPathValueFilter { JsonPathValueIsInlined: true } &&
                    leaf.Values().Any(v => v.Value.Equals(value)))
                {
                    throw new Marten.Exceptions.BadLinqExpressionException(
                        "string.Contains()/EndsWith() and case-insensitive string comparisons inside a collection predicate embed the search text in the SQL, so a compiled query cannot re-bind the value between executions. Use a case-sensitive StartsWith(), move the filter out of the compiled query, or query with session.Query<T>() instead.");
                }
            }

            usage.QueryMember = member;
            return true;
        }

        return false;
    }

    private static IEnumerable<ICollectionAware> collectLeaves(ISqlFragment fragment)
    {
        switch (fragment)
        {
            case CompoundWhereFragment compound:
                foreach (var child in compound.Children)
                foreach (var leaf in collectLeaves(child))
                {
                    yield return leaf;
                }

                break;

            case ICollectionAware aware:
                yield return aware;
                break;
        }
    }

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        // Same ordering caveat as ChildCollectionJsonPathCountFilter: BuildSetter can
        // run before Apply() has rendered the SQL and populated _dict, so capture
        // deferred accessors; the dictionary and usages are shared by reference.
        var dictRef = new Func<Dictionary<string, object>?>(() => _dict);
        var usagesRef = new Func<List<DictionaryValueUsage>?>(() => _usages);
        var serializer = _serializer;
        return (parameter, query) =>
        {
            var payload = CompiledQueryDictionaryBuilder.Build(dictRef(), usagesRef(), query, default);
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameter.Value = payload is null ? DBNull.Value : (object)serializer.ToCleanJson(payload);
        };
    }

    public string? ParameterName { get; private set; }
}
