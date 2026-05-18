#nullable enable
using System;
using System.Reflection;
using JasperFx.CodeGeneration;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Marker interface for SQL fragment filters whose parameter values need special
/// runtime treatment when threaded through a compiled query — typically because the
/// raw caller value is wrapped (LIKE escaping, <c>%</c>-prefixed) or serialized
/// (JSON for containment / JsonPath) before it hits the <see cref="NpgsqlParameter"/>.
/// </summary>
/// <remarks>
/// Two delivery mechanisms coexist during the runtime-code-generation removal
/// (<see href="https://github.com/JasperFx/marten/issues/4454">#4454</see>):
/// <list type="bullet">
///   <item>
///     <see cref="BuildSetter"/> — the AOT/source-gen path. Returns an
///     <see cref="Action"/> that writes
///     <see cref="NpgsqlParameter.NpgsqlDbType"/> + <see cref="NpgsqlParameter.Value"/>
///     given the user's compiled-query instance. Implementations capture the
///     <see cref="MemberInfo"/> cached during <see cref="TryMatchValue"/> plus any
///     per-filter state (raw value, npgsql type, etc.). Built once per
///     <c>(filter, query-member)</c> pair when <c>CompiledQueryPlan.MatchParameters</c>
///     attaches a filter to a <see cref="ParameterUsage"/>; called once per
///     <c>session.Query(...)</c>.
///   </item>
///   <item>
///     <see cref="GenerateCode"/> — the Roslyn-emit codegen path that
///     <c>CompiledQuerySourceBuilder</c> still drives until phase 1E of #4454
///     deletes that path entirely. New filter implementations should focus on
///     <see cref="BuildSetter"/>; the <see cref="GenerateCode"/> override can be
///     a near-copy that emits the same value-wrapping logic into the generated
///     handler class.
///   </item>
/// </list>
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

    void GenerateCode(GeneratedMethod method, int parameterIndex, string parametersVariableName);

    string ParameterName { get; }
}
