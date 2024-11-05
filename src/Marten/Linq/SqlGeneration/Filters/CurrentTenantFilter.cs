#nullable enable
using System;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration.Filters;

public class CurrentTenantFilter: ISqlFragment
{
    public static readonly CurrentTenantFilter Instance = new();

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        if (builder.TenantId.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(builder), "There is no TenantId on this builder");
        }

        builder.Append("d.");
        builder.Append(TenantIdColumn.Name);
        builder.Append(" = ");
        builder.AppendParameter(builder.TenantId);
    }
}
