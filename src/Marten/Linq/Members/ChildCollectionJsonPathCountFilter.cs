#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using Marten.Internal.CompiledQueries;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Core.Serialization;
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
            ParameterName = builder.LastParameterName;

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

    private bool _hasGenerated;
    private Dictionary<string, object> _dict;

    public void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName)
    {
        if (_hasGenerated)
        {
            return;
        }

        _hasGenerated = true;

        var top = new DictionaryDeclaration();
        top.ReadDictionary(_dict, _usages);

        method.Frames.Add(new WriteSerializedJsonParameterFrame(parametersVariableName, parameterIndex, top));
    }

    public string? ParameterName { get; private set; }
}
