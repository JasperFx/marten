#nullable enable
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Shared base for source-gen-driven compiled-query handlers. Owns the
/// <see cref="ConfigureCommand"/> loop that replays the
/// <see cref="CompiledQueryPlan.Commands"/> against the
/// <see cref="CompiledQueryHandlerDescriptor.BindParameter"/> delegate emitted
/// by <c>Marten.SourceGenerator</c> — the AOT-safe replacement for the
/// per-query Roslyn emit in <c>CompiledQuerySourceBuilder</c>.
/// </summary>
/// <remarks>
/// Concrete subclasses differ only in how they materialise the inner
/// <see cref="IQueryHandler{TOut}"/> per call:
/// <list type="bullet">
///   <item><see cref="SourceGeneratedStatelessHandler{TOut}"/> — passes the
///         prototype straight through (mirrors <c>StatelessCompiledQuery</c>).</item>
///   <item><see cref="SourceGeneratedClonedHandler{TOut}"/> — clones via
///         <see cref="IMaybeStatefulHandler.CloneForSession"/> per call
///         (mirrors <c>ClonedCompiledQuery</c>).</item>
///   <item><see cref="SourceGeneratedComplexHandler{TOut}"/> — clones (if
///         stateful) and wraps with <see cref="IncludeQueryHandler{T}"/> per
///         call (mirrors <c>ComplexCompiledQuery</c>).</item>
/// </list>
/// All include-reader construction + statistics lookup goes through the
/// generator-emitted delegates on <see cref="CompiledQueryHandlerDescriptor"/>
/// — no <c>MakeGenericMethod</c>, no reflective member reads.
/// </remarks>
internal abstract class SourceGeneratedCompiledQueryHandlerBase<TOut>: IQueryHandler<TOut>
{
    protected readonly object _query;
    protected readonly CompiledQueryPlan _plan;
    protected readonly CompiledQueryHandlerDescriptor _descriptor;
    protected readonly bool _enumAsString;

    protected SourceGeneratedCompiledQueryHandlerBase(
        object query,
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
    {
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
    public abstract TOut Handle(DbDataReader reader, IMartenSession session);

    public abstract Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token);

    public abstract Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token);
}

/// <summary>
/// Stateless shape — the handler prototype handles reads directly with no
/// per-session cloning. Equivalent to the codegen-emitted
/// <see cref="StatelessCompiledQuery{TOut, TQuery}"/>.
/// </summary>
internal sealed class SourceGeneratedStatelessHandler<TOut>: SourceGeneratedCompiledQueryHandlerBase<TOut>
{
    private readonly IQueryHandler<TOut> _inner;

    public SourceGeneratedStatelessHandler(
        IQueryHandler<TOut> inner,
        object query,
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
        : base(query, plan, descriptor, enumAsString)
    {
        _inner = inner;
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public override TOut Handle(DbDataReader reader, IMartenSession session)
        => _inner.Handle(reader, session);

    public override Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        => _inner.HandleAsync(reader, session, token);

    public override Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        => _inner.StreamJson(stream, reader, token);
}

/// <summary>
/// Cloneable shape — handler prototype is an <see cref="IMaybeStatefulHandler"/>
/// that needs cloning per call (carries reader-position state, statistics,
/// etc.). Equivalent to the codegen-emitted <c>ClonedCompiledQuery</c>.
/// </summary>
internal sealed class SourceGeneratedClonedHandler<TOut>: SourceGeneratedCompiledQueryHandlerBase<TOut>
{
    private readonly IMaybeStatefulHandler _statefulInner;
    private readonly QueryStatistics? _statistics;

    public SourceGeneratedClonedHandler(
        IMaybeStatefulHandler statefulInner,
        QueryStatistics? statistics,
        object query,
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
        : base(query, plan, descriptor, enumAsString)
    {
        _statefulInner = statefulInner;
        _statistics = statistics;
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public override TOut Handle(DbDataReader reader, IMartenSession session)
    {
        var inner = (IQueryHandler<TOut>)_statefulInner.CloneForSession(session, _statistics!);
        return inner.Handle(reader, session);
    }

    public override Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        var inner = (IQueryHandler<TOut>)_statefulInner.CloneForSession(session, _statistics!);
        return inner.HandleAsync(reader, session, token);
    }

    public override Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        => _statefulInner.StreamJson(stream, reader, token);
}

/// <summary>
/// Complex shape — query has <see cref="CompiledQueryPlan.IncludeMembers"/>.
/// Each call clones the stateful inner (if needed) and wraps it in an
/// <see cref="IncludeQueryHandler{T}"/> built from the consumer's include
/// targets. Equivalent to the codegen-emitted <c>ComplexCompiledQuery</c>.
/// All Include reader construction is delegated to the generator-emitted
/// <see cref="CompiledQueryHandlerDescriptor.AttachIncludeReaders"/> closure
/// — typed factory calls, no <c>MakeGenericMethod</c>.
/// </summary>
internal sealed class SourceGeneratedComplexHandler<TOut>: SourceGeneratedCompiledQueryHandlerBase<TOut>
{
    private readonly IMaybeStatefulHandler? _statefulInner;
    private readonly IQueryHandler<TOut>? _statelessInner;
    private readonly QueryStatistics? _statistics;

    public SourceGeneratedComplexHandler(
        IQueryHandler innerPrototype,
        QueryStatistics? statistics,
        object query,
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
        : base(query, plan, descriptor, enumAsString)
    {
        _statistics = statistics;

        // Track whether we need to clone-per-call. Mirrors the
        // CompiledQuerySourceBuilder.buildHandlerMethod branch on
        // _plan.HandlerPrototype is IMaybeStatefulHandler h && h.DependsOnDocumentSelector().
        if (innerPrototype is IMaybeStatefulHandler stateful && stateful.DependsOnDocumentSelector())
        {
            _statefulInner = stateful;
        }
        else
        {
            _statelessInner = (IQueryHandler<TOut>)innerPrototype;
        }
    }

    private IQueryHandler<TOut> BuildIncludingHandler(IMartenSession session)
    {
        var inner = _statefulInner is not null
            ? (IQueryHandler<TOut>)_statefulInner.CloneForSession(session, _statistics!)
            : _statelessInner!;

        // Descriptor-driven path — the generator emits typed Include.ReaderTo*
        // factory calls per consumer-declared include member. No reflection.
        var readers = _descriptor.AttachIncludeReaders(session, _query);
        return new IncludeQueryHandler<TOut>(inner, readers);
    }

    [Obsolete(QuerySession.SynchronousRemoval)]
    public override TOut Handle(DbDataReader reader, IMartenSession session)
        => BuildIncludingHandler(session).Handle(reader, session);

    public override Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        => BuildIncludingHandler(session).HandleAsync(reader, session, token);

    public override Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        => throw new NotSupportedException(
            "JSON streaming is not supported in combination with Include() operations on compiled queries.");
}

/// <summary>
/// <see cref="ICompiledQuerySource"/> that hands out the right
/// <see cref="SourceGeneratedCompiledQueryHandlerBase{TOut}"/> subclass for
/// each query's <see cref="CompiledQueryPlan"/> shape. One source is cached
/// per <c>(QueryType, DocumentTracking)</c> combination, exactly like the
/// existing codegen-backed source.
/// </summary>
internal sealed class SourceGeneratedCompiledQuerySource<TOut>: ICompiledQuerySource
{
    private readonly CompiledQueryPlan _plan;
    private readonly CompiledQueryHandlerDescriptor _descriptor;
    private readonly bool _enumAsString;
    private readonly Shape _shape;

    private enum Shape { Stateless, Cloned, Complex }

    public SourceGeneratedCompiledQuerySource(
        CompiledQueryPlan plan,
        CompiledQueryHandlerDescriptor descriptor,
        bool enumAsString)
    {
        _plan = plan;
        _descriptor = descriptor;
        _enumAsString = enumAsString;
        _shape = DetermineShape(plan);
    }

    public Type QueryType => _descriptor.QueryType;

    public IQueryHandler Build(object query, IMartenSession session)
    {
        return _shape switch
        {
            Shape.Stateless => BuildStateless(query),
            Shape.Cloned => BuildCloned(query),
            Shape.Complex => BuildComplex(query),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private SourceGeneratedStatelessHandler<TOut> BuildStateless(object query)
    {
        if (_plan.HandlerPrototype is not IQueryHandler<TOut> inner)
        {
            throw new InvalidOperationException(
                $"Expected HandlerPrototype to implement IQueryHandler<{typeof(TOut).Name}> for compiled query " +
                $"{_descriptor.QueryType.FullName}; got {_plan.HandlerPrototype?.GetType().Name ?? "null"}.");
        }
        return new SourceGeneratedStatelessHandler<TOut>(inner, query, _plan, _descriptor, _enumAsString);
    }

    private SourceGeneratedClonedHandler<TOut> BuildCloned(object query)
    {
        if (_plan.HandlerPrototype is not IMaybeStatefulHandler stateful)
        {
            throw new InvalidOperationException(
                $"Cloned shape requires HandlerPrototype to implement IMaybeStatefulHandler; got " +
                $"{_plan.HandlerPrototype?.GetType().Name ?? "null"}.");
        }
        var statistics = _descriptor.ReadStatistics(query);
        return new SourceGeneratedClonedHandler<TOut>(stateful, statistics, query, _plan, _descriptor, _enumAsString);
    }

    private SourceGeneratedComplexHandler<TOut> BuildComplex(object query)
    {
        var statistics = _descriptor.ReadStatistics(query);
        return new SourceGeneratedComplexHandler<TOut>(
            _plan.HandlerPrototype, statistics, query, _plan, _descriptor, _enumAsString);
    }

    private static Shape DetermineShape(CompiledQueryPlan plan)
    {
        if (plan.IncludeMembers.Count > 0) return Shape.Complex;
        if (plan.HandlerPrototype is IMaybeStatefulHandler stateful
            && stateful.DependsOnDocumentSelector()) return Shape.Cloned;
        return Shape.Stateless;
    }
}
