#nullable enable
using System;
using Npgsql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Source-gen-emitted contract for a single compiled query type. One descriptor
/// is registered with <see cref="CompiledQueryHandlerRegistry"/> per
/// <c>ICompiledQuery&lt;TDoc, TOut&gt;</c> implementation in a consumer
/// assembly marked with <c>[JasperFxAssembly]</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the runtime hand-off point for #4405's compiled-query source-gen
/// PoC. The <see cref="BindParameter"/> delegate replaces the per-property
/// reflection currently emitted by <c>CompiledQuerySourceBuilder</c> +
/// <c>ParameterUsage.GenerateCode</c> with a direct field/property read from
/// the consumer's query type. The generator's <c>BindParameter</c> body lives
/// in the static <c>{Query}_CompiledQueryHandler</c> class it emits; a
/// zero-allocation <c>static</c> lambda adapts that strongly-typed call to
/// this <see cref="System.Action"/> shape so the registry can hold
/// heterogeneous query types.
/// </para>
/// <para>
/// The pre-classified member name arrays mirror
/// <c>CompiledQueryPlan.QueryMembers</c> / <c>IncludeMembers</c> /
/// <c>StatisticsMember</c>. The runtime planner uses them to skip reflection
/// when matching parameter usages to query members during
/// <c>CompiledQueryPlan.MatchParameters</c>.
/// </para>
/// </remarks>
public sealed class CompiledQueryHandlerDescriptor
{
    /// <summary>
    /// Constructs a descriptor for a generator-emitted compiled query handler.
    /// </summary>
    /// <param name="queryType">CLR type of the <c>ICompiledQuery&lt;TDoc,TOut&gt;</c> implementation.</param>
    /// <param name="docType">CLR type of <c>TDoc</c> on the compiled query interface.</param>
    /// <param name="outputType">CLR type of <c>TOut</c> on the compiled query interface.</param>
    /// <param name="parameterMemberNames">
    /// Names of public fields/properties on the query type that bind to SQL parameters.
    /// Order is not significant — the runtime planner matches names against
    /// parameter usages derived from <c>LinqQueryParser</c>.
    /// </param>
    /// <param name="includeMemberNames">
    /// Names of public fields/properties on the query type that carry Include readers
    /// (<see cref="Action{T}"/>, <see cref="System.Collections.Generic.IList{T}"/>,
    /// <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>).
    /// </param>
    /// <param name="statisticsMemberName">
    /// Name of the public field/property carrying a <c>QueryStatistics</c> instance, or
    /// <see langword="null"/> if the query type has no statistics member.
    /// </param>
    /// <param name="bindParameter">
    /// Zero-allocation hot-path delegate that writes <c>parameter.NpgsqlDbType</c> +
    /// <c>parameter.Value</c> from the named member of <paramref name="bindParameter"/>'s
    /// boxed <c>query</c> argument. The <c>enumAsString</c> flag is the consumer
    /// session's <c>StoreOptions.Serializer().EnumStorage == EnumStorage.AsString</c>;
    /// the generator emits both branches in-line and dispatches on this flag so the
    /// handler doesn't need to be regenerated per store configuration.
    /// </param>
    public CompiledQueryHandlerDescriptor(
        Type queryType,
        Type docType,
        Type outputType,
        string[] parameterMemberNames,
        string[] includeMemberNames,
        string? statisticsMemberName,
        Action<NpgsqlParameter, object, string, bool> bindParameter)
    {
        QueryType = queryType ?? throw new ArgumentNullException(nameof(queryType));
        DocType = docType ?? throw new ArgumentNullException(nameof(docType));
        OutputType = outputType ?? throw new ArgumentNullException(nameof(outputType));
        ParameterMemberNames = parameterMemberNames ?? throw new ArgumentNullException(nameof(parameterMemberNames));
        IncludeMemberNames = includeMemberNames ?? throw new ArgumentNullException(nameof(includeMemberNames));
        StatisticsMemberName = statisticsMemberName;
        BindParameter = bindParameter ?? throw new ArgumentNullException(nameof(bindParameter));
    }

    public Type QueryType { get; }
    public Type DocType { get; }
    public Type OutputType { get; }
    public string[] ParameterMemberNames { get; }
    public string[] IncludeMemberNames { get; }
    public string? StatisticsMemberName { get; }
    public Action<NpgsqlParameter, object, string, bool> BindParameter { get; }
}
