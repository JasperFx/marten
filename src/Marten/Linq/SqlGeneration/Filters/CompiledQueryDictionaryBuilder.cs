#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
/// Runtime dictionary builder used by compiled-query <see cref="ICompiledQueryAwareFilter"/>
/// implementations whose <see cref="ICompiledQueryAwareFilter.BuildSetter"/> writes
/// a serialized JSON payload (containment + JsonPath count filters). Mirrors the
/// codegen-time <see cref="DictionaryDeclaration"/> /
/// <see cref="WriteSerializedJsonParameterFrame"/> emit: the captured
/// <c>data</c> tree carries the constant values seen at LINQ-parse time;
/// each <see cref="DictionaryValueUsage"/> in <paramref name="usages"/> maps a
/// constant to the <c>MemberInfo</c> that supplied it on the compiled-query
/// instance. At runtime we walk <paramref name="data"/> and substitute each
/// leaf with the corresponding member read off <paramref name="query"/>.
/// </summary>
/// <remarks>
/// AOT-clean: pure reflection over user-supplied <c>MemberInfo</c>, no Expression
/// compile / no MakeGenericMethod. The reflection cost is paid once per
/// <c>session.Query(compiledQuery)</c> invocation; the dictionary shape is small
/// enough that the savings of a compiled-getter accessor wouldn't justify the
/// additional FEC dependency on the hot path.
/// </remarks>
internal static class CompiledQueryDictionaryBuilder
{
    internal static Dictionary<string, object?> Build(
        Dictionary<string, object> data,
        List<DictionaryValueUsage> usages,
        object query)
    {
        var result = new Dictionary<string, object?>(data.Count);
        foreach (var pair in data)
        {
            result[pair.Key] = BuildValue(pair.Value, usages, query);
        }

        return result;
    }

    internal static Dictionary<string, object?>? Build(
        Dictionary<string, object>? data,
        List<DictionaryValueUsage>? usages,
        object query,
        bool _)
    {
        if (data is null) return null;
        return Build(data, usages ?? new List<DictionaryValueUsage>(), query);
    }

    private static object? BuildValue(object value, List<DictionaryValueUsage> usages, object query)
    {
        switch (value)
        {
            case Dictionary<string, object> nested:
                return Build(nested, usages, query);

            case object[] array:
                return BuildArray(array, usages, query);

            default:
                return ResolveLeaf(value, usages, query);
        }
    }

    private static object?[] BuildArray(object[] array, List<DictionaryValueUsage> usages, object query)
    {
        var result = new object?[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            result[i] = array[i] switch
            {
                Dictionary<string, object> nested => Build(nested, usages, query),
                object[] innerArray => BuildArray(innerArray, usages, query),
                var leaf => ResolveLeaf(leaf, usages, query)
            };
        }

        return result;
    }

    private static object? ResolveLeaf(object value, List<DictionaryValueUsage> usages, object query)
    {
        // The captured constant value is the LINQ-parse-time snapshot. If a
        // matching DictionaryValueUsage has a QueryMember attached, the
        // compiled-query plumbing means we should pull the live value off the
        // user's query instance instead — that's what the codegen path emits
        // as `_query.<Member>`.
        var usage = usages.FirstOrDefault(u => Equals(u.Value, value));
        if (usage?.QueryMember is { } member)
        {
            return Marten.Internal.CompiledQueries.CompiledQueryMemberReader.Read(member, query);
        }

        return value;
    }
}
