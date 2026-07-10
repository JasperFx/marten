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
///     Translates a child collection Any(predicate) that cannot be reduced to JSONB
///     containment (@>) into a single jsonb_path_exists() predicate instead of the
///     much more expensive "explode the collection into a CTE and correlate on ctid"
///     sub-query strategy. Values are passed through the jsonpath vars parameter so
///     the SQL stays parameterized.
/// </summary>
internal class JsonPathExistsFilter: ISqlFragment, ICollectionAwareFilter, ICompiledQueryAwareFilter,
    IReversibleWhereFragment
{
    private readonly ISqlFragment _predicate;
    private readonly ISerializer _serializer;
    private string _collectionPath;
    private string _documentColumn;
    private Dictionary<string, object>? _dict;
    private List<DictionaryValueUsage>? _usages;

    private JsonPathExistsFilter(ICollectionMember collection, ISqlFragment predicate, ISerializer serializer)
    {
        CollectionMember = collection;
        _predicate = predicate;
        _serializer = serializer;
        _collectionPath = collection.WriteJsonPath();
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
        writePredicate(builder, _predicate);

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

    public ISqlFragment MoveUnder(ICollectionMember ancestorCollection)
    {
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
