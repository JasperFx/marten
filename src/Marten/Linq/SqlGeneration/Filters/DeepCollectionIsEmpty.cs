#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Linq.Members;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

/// <summary>
///     "Some ancestor element whose nested collection is empty". Unlike the
///     not-empty case this cannot flatten to a total length check — a flattened
///     length of zero would mean EVERY ancestor's collection is empty, not SOME
/// </summary>
internal class DeepCollectionIsEmpty: ISqlFragment
{
    public DeepCollectionIsEmpty(List<IQueryableMember> path)
    {
        Path = path;
    }

    public List<IQueryableMember> Path { get; }

    public void Apply(ICommandBuilder builder)
    {
        var segments = Path.Where(x => x.JsonPathSegment.IsNotEmpty()).ToArray();
        var final = segments.Last().JsonPathSegment.Replace("[*]", "");

        builder.Append("d.data @? '$");
        foreach (var member in segments.Take(segments.Length - 1))
        {
            builder.Append(".");
            builder.Append(member.JsonPathSegment);
        }

        builder.Append(" ? (@.");
        builder.Append(final);
        builder.Append(" == null || @.");
        builder.Append(final);
        builder.Append(".size() == 0)'");
    }
}
