#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

/// <summary>
/// The count itself: <c>jsonb_array_length(jsonb_path_query_array(d.data, '$.Member ? (predicate)'))</c>,
/// with no comparison attached.
///
/// <para>
/// #5223: this used to be inlined in <see cref="ChildCollectionJsonPathCountFilter" />, which only ever
/// rendered it as the left side of a <c>Where()</c> comparison. A <c>Select()</c> projection needs the
/// same scalar on its own, so the expression lives here and the filter composes it.
/// </para>
/// </summary>
internal class ChildCollectionJsonPathCount: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly ICollectionAware[] _filters;
    private readonly ICollectionMember _member;
    private readonly ISerializer _serializer;
    private Dictionary<string, object>? _dict;
    private List<DictionaryValueUsage>? _usages;

    public ChildCollectionJsonPathCount(ICollectionMember member, ISerializer serializer,
        IEnumerable<ICollectionAware> filters)
    {
        _member = member;
        _serializer = serializer;
        _filters = filters.ToArray();
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("jsonb_array_length(jsonb_path_query_array(d.data, '$.");
        _member.WriteJsonPath(builder);
        builder.Append(" ? (");

        _dict = new Dictionary<string, object>();
        _filters[0].BuildJsonPathFilter(builder, _dict);

        for (var i = 1; i < _filters.Length; i++)
        {
            builder.Append(" && ");
            _filters[i].BuildJsonPathFilter(builder, _dict);
        }

        if (_dict.Count == 0)
        {
            builder.Append(")')) ");
        }
        else
        {
            builder.Append(")', ");
            builder.AppendParameter(_serializer.ToCleanJson(_dict), NpgsqlDbType.Jsonb);
            ParameterName = builder.LastParameterName!;

            builder.Append(")) ");
        }
    }

    public bool TryMatchValue(object value, MemberInfo member)
    {
        _usages ??= _filters.SelectMany(x => x.Values()).ToList();

        var usage = _usages.FirstOrDefault(x => x.Value.Equals(value));
        if (usage != null)
        {
            usage.QueryMember = member;
            return true;
        }

        return false;
    }

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        // Apply() may not have been invoked yet at the time MatchParameters calls
        // BuildSetter (Apply runs when Marten renders the SQL command, plan matching
        // runs at session.Query time — opposite order). Snapshot what we have now;
        // the dict + usages list are filled by Apply / TryMatchValue and shared by
        // reference, so the captured locals see the post-Apply state at invocation.
        var dictRef = new Func<Dictionary<string, object>?>(() => _dict);
        var usagesRef = new Func<List<DictionaryValueUsage>?>(() => _usages);
        var serializer = _serializer;
        return (parameter, query) =>
        {
            var payload = CompiledQueryDictionaryBuilder.Build(dictRef(), usagesRef(), query, default);
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameter.Value = payload is null ? DBNull.Value : serializer.ToCleanJson(payload);
        };
    }

    public string ParameterName { get; private set; } = null!;
}

/// <summary>
/// <c>Where(x =&gt; x.Children.Count(c =&gt; ...) &gt; n)</c>: the count expression above, compared to a
/// constant.
/// </summary>
internal class ChildCollectionJsonPathCountFilter: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly ConstantExpression _constant;
    private readonly ChildCollectionJsonPathCount _count;
    private readonly string _op;

    public ChildCollectionJsonPathCountFilter(ICollectionMember member, ISerializer serializer,
        IEnumerable<ICollectionAware> filters, string op, ConstantExpression constant)
    {
        _count = new ChildCollectionJsonPathCount(member, serializer, filters);
        _op = op;
        _constant = constant;
    }

    public void Apply(ICommandBuilder builder)
    {
        _count.Apply(builder);

        builder.Append(_op);
        builder.Append(" ");
        builder.AppendParameter(_constant.Value());
    }

    public bool TryMatchValue(object value, MemberInfo member) => _count.TryMatchValue(value, member);

    public Action<NpgsqlParameter, object> BuildSetter() => _count.BuildSetter();

    public string ParameterName => _count.ParameterName;
}
