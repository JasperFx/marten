using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

internal class DeepCollectionIsNotEmpty: ISqlFragment
{
    public DeepCollectionIsNotEmpty(List<IQueryableMember> path)
    {
        Path = path;
    }

    public List<IQueryableMember> Path { get; }

    public void Apply(CommandBuilder builder)
    {
        builder.Append("jsonb_array_length(jsonb_path_query_array(d.data, '$");
        foreach (var member in Path.Where(x => x.JsonPathSegment.IsNotEmpty()))
        {
            builder.Append(".");
            builder.Append(member.JsonPathSegment);
        }

        builder.Append("')) > 0");
    }

    public bool Contains(string sqlText)
    {
        return false;
    }
}
