#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Fetching;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;

namespace Marten.Testing.OtherAssembly.CustomFetchPlanners;

/// <summary>
/// Registers <see cref="ExternalFetchPlanner" /> the way an application would. The
/// <c>FetchPlanners.Add(...)</c> call below is the actual subject of this assembly: it only
/// compiles while <c>StoreOptions.Projections.FetchPlanners</c> is public, and this assembly is
/// deliberately *not* in Marten's InternalsVisibleTo list.
/// </summary>
public static class ExternalFetchPlannerExtensions
{
    public static ExternalFetchPlanner UseExternalFetchPlanner(this StoreOptions options, Type aggregateType)
    {
        var planner = new ExternalFetchPlanner(aggregateType);
        options.Projections.FetchPlanners.Add(planner);
        return planner;
    }
}

/// <summary>
/// Lives in this assembly on purpose. Marten does *not* grant InternalsVisibleTo to
/// Marten.Testing.OtherAssembly, so this file only compiles while every type an
/// application needs to write its own fetch plan is genuinely public:
/// StoreOptions.Projections.FetchPlanners, IFetchPlanner, IAggregateFetchPlan&lt;TDoc, TId&gt;,
/// IEventIdentityStrategy&lt;TId&gt;, DocumentSessionBase, QuerySession and IQueryHandler&lt;T&gt;.
/// Narrow any of those back to internal and the build breaks here rather than silently
/// removing the extension point.
/// </summary>
public class ExternalFetchPlanner: IFetchPlanner
{
    private readonly Type _aggregateType;

    public ExternalFetchPlanner(Type aggregateType)
    {
        _aggregateType = aggregateType;
    }

    /// <summary>
    /// Incremented every time Marten asks this planner for a plan, so a test can prove the
    /// planner is consulted at all — including for aggregate types it declines.
    /// </summary>
    public int TryMatchCount { get; private set; }

    public bool TryMatch<TDoc, TId>(IEventIdentityStrategy<TId> identity, StoreOptions options,
        [NotNullWhen(true)] out IAggregateFetchPlan<TDoc, TId>? plan) where TDoc : class where TId : notnull
    {
        TryMatchCount++;

        if (typeof(TDoc) != _aggregateType)
        {
            // Declining leaves Marten's built-in planners to resolve this aggregate type
            // exactly as they would without this planner registered.
            plan = default;
            return false;
        }

        plan = new ExternalFetchPlan<TDoc, TId>();
        return true;
    }
}

/// <summary>
/// A deliberately inert plan: every entry point throws <see cref="ExternalFetchPlanWasUsedException" />
/// so a test can assert which plan Marten actually resolved. A real custom plan would use the
/// public primitives on <see cref="IEventIdentityStrategy{TId}" /> — BuildCommandForReadingVersionForStream,
/// BuildEventQueryHandler (which takes an optional ISqlFragment filter) and StartStream/AppendToStream —
/// against <see cref="DocumentSessionBase.ExecuteReaderAsync(Npgsql.NpgsqlBatch, CancellationToken)" />.
/// Marten's own FetchLivePlan / FetchInlinedPlan / FetchAsyncPlan are the reference implementations.
/// </summary>
public class ExternalFetchPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
{
    public ProjectionLifecycle Lifecycle => ProjectionLifecycle.Live;

    public Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default) => throw new ExternalFetchPlanWasUsedException();

    public Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default) => throw new ExternalFetchPlanWasUsedException();

    public ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
        => throw new ExternalFetchPlanWasUsedException();

    public ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation)
        => throw new ExternalFetchPlanWasUsedException();

    public Task<bool> StreamForReading(DocumentSessionBase session, TId id, Stream destination,
        CancellationToken cancellation) => throw new ExternalFetchPlanWasUsedException();

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id,
        long expectedStartingVersion) => throw new ExternalFetchPlanWasUsedException();

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
        => throw new ExternalFetchPlanWasUsedException();

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
        => throw new ExternalFetchPlanWasUsedException();
}

public class ExternalFetchPlanWasUsedException: Exception
{
    public ExternalFetchPlanWasUsedException(): base("The externally registered fetch plan was used")
    {
    }
}
