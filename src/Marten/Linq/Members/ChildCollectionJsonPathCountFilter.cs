using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration.Filters;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

internal class ChildCollectionJsonPathCountFilter: ISqlFragment
{
    private readonly ConstantExpression _constant;
    private readonly ICollectionAware[] _filters;
    private readonly ICollectionMember _member;
    private readonly string _op;
    private readonly ISerializer _serializer;

    public ChildCollectionJsonPathCountFilter(ICollectionMember member, ISerializer serializer,
        IEnumerable<ICollectionAware> filters, string op, ConstantExpression constant)
    {
        _member = member;
        _serializer = serializer;
        _op = op;
        _constant = constant;
        _filters = filters.ToArray();
    }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("jsonb_array_length(jsonb_path_query_array(d.data, '$.");
        _member.WriteJsonPath(builder);
        builder.Append(" ? (");


        var dict = new Dictionary<string, object>();
        _filters[0].BuildJsonPathFilter(builder, dict);

        for (var i = 1; i < _filters.Length; i++)
        {
            builder.Append(" && ");
            _filters[i].BuildJsonPathFilter(builder, dict);
        }

        if (dict.Count == 0)
        {
            builder.Append(")')) ");
        }
        else
        {
            builder.Append(")', ");
            builder.AppendParameter(_serializer.ToCleanJson(dict), NpgsqlDbType.Jsonb);
            builder.Append(")) ");
        }

        builder.Append(_op);
        builder.Append(" ");
        builder.AppendParameter(_constant.Value());
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
