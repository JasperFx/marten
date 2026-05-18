#nullable enable
using System;
using System.Reflection;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Marker interface for SQL fragment filters whose parameter values need special
/// runtime treatment when threaded through a compiled query — typically because the
/// raw caller value is wrapped (LIKE escaping, <c>%</c>-prefixed) or serialized
/// (JSON for containment / JsonPath) before it hits the <see cref="NpgsqlParameter"/>.
/// </summary>
/// <remarks>
/// <see cref="BuildSetter"/> returns an <see cref="Action"/> that writes
/// <see cref="NpgsqlParameter.NpgsqlDbType"/> + <see cref="NpgsqlParameter.Value"/>
/// given the user's compiled-query instance. Implementations capture the
/// <see cref="MemberInfo"/> cached during <see cref="TryMatchValue"/> plus any
/// per-filter state (raw value, npgsql type, etc.). Built once per
/// <c>(filter, query-member)</c> pair when <c>CompiledQueryPlan.MatchParameters</c>
/// attaches a filter to a <see cref="ParameterUsage"/>; called once per
/// <c>session.Query(...)</c>.
/// </remarks>
public interface ICompiledQueryAwareFilter
{
    bool TryMatchValue(object value, MemberInfo member);

    /// <summary>
    /// Returns a delegate that writes the parameter's npgsql type + value from the
    /// matched member on the compiled-query instance. Closes over the
    /// <see cref="MemberInfo"/> captured by the most recent successful
    /// <see cref="TryMatchValue"/> call plus any per-filter state. Called once per
    /// <c>session.Query(compiledQuery)</c> invocation, never per row.
    /// </summary>
    Action<NpgsqlParameter, object> BuildSetter();

    string ParameterName { get; }
}
