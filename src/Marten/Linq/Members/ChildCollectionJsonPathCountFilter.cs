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

internal class ChildCollectionJsonPathCountFilter: ISqlFragment, ICompiledQueryAwareFilter
{
    private readonly ConstantExpression _constant;
    private readonly ICollectionAware[] _filters;
    private readonly ICollectionMember _member;
    private readonly string _op;
    private readonly ISerializer _serializer;
    private List<DictionaryValueUsage>? _usages;

    public ChildCollectionJsonPathCountFilter(ICollectionMember member, ISerializer serializer,
        IEnumerable<ICollectionAware> filters, string op, ConstantExpression constant)
    {
        _member = member;
        _serializer = serializer;
        _op = op;
        _constant = constant;
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

        builder.Append(_op);
        builder.Append(" ");
        builder.AppendParameter(_constant.Value());
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

    private Dictionary<string, object> _dict;

    public Action<NpgsqlParameter, object> BuildSetter()
    {
        // Apply() may not have been invoked yet at the time MatchParameters calls
        // BuildSetter (Apply runs when Marten renders the SQL command, plan matching
        // runs at session.Query time — opposite order). Snapshot what we have now;
        // the dict + usages list are filled by Apply / TryMatchValue and shared by
        // reference, so the captured locals see the post-Apply state at invocation.
        var dictRef = new System.Func<Dictionary<string, object>?>(() => _dict);
        var usagesRef = new System.Func<List<DictionaryValueUsage>?>(() => _usages);
        var serializer = _serializer;
        return (parameter, query) =>
        {
            var payload = Marten.Linq.SqlGeneration.Filters.CompiledQueryDictionaryBuilder.Build(
                dictRef(), usagesRef(), query, default);
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameter.Value = payload is null ? System.DBNull.Value : (object)serializer.ToCleanJson(payload);
        };
    }

    public string ParameterName { get; private set; }
}
