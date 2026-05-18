using System;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.CompiledQueries;

internal class ParameterUsage
{
    public bool IsTenant { get; set; }
    public int Index { get; }
    public NpgsqlParameter Parameter { get; }

    public ParameterUsage(int index, string name, object value, NpgsqlDbType? dbType = null)
    {
        Index = index;
        Parameter = new NpgsqlParameter { Value = value, ParameterName = name};
        if (dbType.HasValue) Parameter.NpgsqlDbType = dbType.Value;

        if (value is int) Parameter.NpgsqlDbType = NpgsqlDbType.Integer;

        Name = name;
    }

    public string Name { get;}

    // If none, it's hard-coded
    public IQueryMember Member { get; set; }
    public ICompiledQueryAwareFilter? Filter { get; set; }

    /// <summary>
    /// Cached runtime setter built once per (filter, query-member) pair when
    /// <c>CompiledQueryPlan.MatchParameters</c> attaches a filter — the
    /// source-gen / FEC dispatch path uses this delegate to write the
    /// parameter's value, bypassing the descriptor's generic
    /// <c>BindParameter</c> for filters that need to wrap or serialize the
    /// raw value (LIKE escaping, containment JSON, etc.).
    /// </summary>
    public Action<NpgsqlParameter, object>? Setter { get; set; }
}
