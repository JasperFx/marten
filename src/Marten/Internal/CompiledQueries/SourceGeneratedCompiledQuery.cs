#nullable enable
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Source-gen-driven replacement for the codegen'd <see cref="StatelessCompiledQuery{TOut, TQuery}"/>
/// subclass. Replays the <see cref="CompiledQueryPlan.Commands"/> list at runtime
/// using the <see cref="CompiledQueryHandlerDescriptor.BindParameter"/> delegate
/// emitted by <c>Marten.SourceGenerator</c>, eliminating the per-query Roslyn
/// emit that <c>CompiledQuerySourceBuilder</c> performs today.
/// </summary>
/// <remarks>
/// <para>
/// This is the iteration-3 PoC handler for #4405. Scope: <b>Stateless</b> shape
/// only — queries whose <c>HandlerPrototype</c> is not an
/// <see cref="IMaybeStatefulHandler"/> requiring per-session cloning, and which
/// do not declare <c>IncludeMembers</c>. Cloneable and Complex shapes still
/// take the codegen path via the PoC bridge in
/// <c>CompiledQueryCollection.GetCompiledQuerySourceFor</c>; their source-gen
/// equivalents land in iteration 4.
/// </para>
/// <para>
/// Generic in <typeparamref name="TOut"/> only. The query type is held as
/// <see cref="object"/> because the <see cref="CompiledQueryHandlerDescriptor"/>'s
/// boxing adapter handles the cast inside the source-gen-emitted lambda. That
/// avoids a <c>MakeGenericType(typeof(TOut), queryType)</c> call in the dispatch
/// path — one less reflective emit, one less AOT concern.
/// </para>
/// </remarks>
internal sealed class SourceGeneratedCompiledQueryHandler<TOut>: IQueryHandler<TOut>
{
    private readonly IQueryHandler<TOut> _inner;
    private readonly object _query;
    private readonly CompiledQueryPlan _plan;
    private readonly CompiledQueryHandlerDescriptor _descriptor;
    private readonly bool _enumAsString;

    public SourceGeneratedCompiledQueryHandler(
        IQueryHandler<TOut> inner,
        object query,
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
    {
        _inner = inner;
        _query = query;
        _plan = plan;
        _descriptor = descriptor;
        _enumAsString = enumAsString;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var first = true;
        foreach (var command in _plan.Commands)
        {
            if (!first) builder.StartNewCommand();
            first = false;

            if (command.Parameters.Count == 0)
            {
                builder.Append(command.CommandText);
                continue;
            }

            NpgsqlParameter[] parameters = builder.AppendWithParameters(
                command.CommandText, CompiledQueryPlan.ParameterPlaceholder[0]);

            for (var i = 0; i < command.Parameters.Count; i++)
            {
                var usage = command.Parameters[i];
                var parameter = parameters[i];

                if (usage.IsTenant)
                {
                    parameter.Value = builder.TenantId;
                    continue;
                }

                if (usage.Member?.Member is { } memberInfo)
                {
                    // Source-gen binder path — direct property/field read inside
                    // the generated switch, no reflection.
                    _descriptor.BindParameter(parameter, _query, memberInfo.Name, _enumAsString);
                    continue;
                }

                // Hardcoded literal — preserved from the LinqQueryParser-built plan.
                parameter.Value = usage.Parameter.Value;
                parameter.NpgsqlDbType = usage.Parameter.NpgsqlDbType;
            }
        }
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public TOut Handle(DbDataReader reader, IMartenSession session) => _inner.Handle(reader, session);

    public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        => _inner.HandleAsync(reader, session, token);

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        => _inner.StreamJson(stream, reader, token);
}

/// <summary>
/// <see cref="ICompiledQuerySource"/> that hands out
/// <see cref="SourceGeneratedCompiledQueryHandler{TOut}"/> instances. One source
/// is cached per <c>(QueryType, DocumentTracking)</c> combination, exactly like
/// the existing codegen-backed source.
/// </summary>
internal sealed class SourceGeneratedCompiledQuerySource<TOut>: ICompiledQuerySource
{
    private readonly CompiledQueryPlan _plan;
    private readonly CompiledQueryHandlerDescriptor _descriptor;
    private readonly bool _enumAsString;

    public SourceGeneratedCompiledQuerySource(
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
    {
        _plan = plan;
        _descriptor = descriptor;
        _enumAsString = enumAsString;
    }

    public Type QueryType => _descriptor.QueryType;

    public IQueryHandler Build(object query, IMartenSession session)
    {
        if (_plan.HandlerPrototype is not IQueryHandler<TOut> inner)
        {
            throw new InvalidOperationException(
                $"Expected HandlerPrototype to implement IQueryHandler<{typeof(TOut).Name}> for compiled query " +
                $"{_descriptor.QueryType.FullName}; got {_plan.HandlerPrototype?.GetType().Name ?? "null"}.");
        }

        return new SourceGeneratedCompiledQueryHandler<TOut>(
            inner, query, _plan, _descriptor, _enumAsString);
    }
}
