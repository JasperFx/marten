#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;

namespace Marten.Events.Fetching;

/// <summary>
/// Adapts a fetch plan built for the raw stream identity so it can be addressed by a strong-typed
/// identifier wrapping that identity.
/// </summary>
/// <remarks>
/// <para>
/// A strong-typed id such as <c>readonly record struct PaymentId(Guid Value)</c> is not a natural
/// key — it *is* the stream identity, just wrapped. Before #5144 the generic
/// <c>FetchForWriting&lt;T, TId&gt;</c> overloads had nowhere to put it: <c>TId</c> is neither
/// <c>Guid</c> nor <c>string</c>, so planning fell into the natural-key branch, which passes a null
/// identity strategy, and the lifecycle planners matched anyway and stored the null. The result was
/// a bare <see cref="NullReferenceException"/> from inside the plan.
/// </para>
/// <para>
/// Everything about the fetch is identical once the identity is unwrapped, so this forwards to the
/// plan for the underlying type rather than duplicating any of it.
/// <see cref="IAggregateFetchPlan{TDoc,TId}"/> is contravariant in <c>TId</c> and every member takes
/// the identity as input, which is what makes a pure forwarder sufficient.
/// </para>
/// </remarks>
internal class UnwrappedIdentityFetchPlan<TDoc, TId, TInner>: IAggregateFetchPlan<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
    where TInner : notnull
{
    private readonly IAggregateFetchPlan<TDoc, TInner> _inner;
    private readonly Func<TId, TInner> _unwrap;

    public UnwrappedIdentityFetchPlan(IAggregateFetchPlan<TDoc, TInner> inner, Func<TId, TInner> unwrap)
    {
        _inner = inner;
        _unwrap = unwrap;
    }

    public ProjectionLifecycle Lifecycle => _inner.Lifecycle;

    public Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
        => _inner.FetchForWriting(session, _unwrap(id), forUpdate, cancellation);

    public Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion, CancellationToken cancellation = default)
        => _inner.FetchForWriting(session, _unwrap(id), expectedStartingVersion, cancellation);

    public ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
        => _inner.FetchForReading(session, _unwrap(id), cancellation);

    public ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation)
        => _inner.ProjectLatest(session, _unwrap(id), cancellation);

    public Task<bool> StreamForReading(DocumentSessionBase session, TId id, Stream destination,
        CancellationToken cancellation)
        => _inner.StreamForReading(session, _unwrap(id), destination, cancellation);

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id,
        long expectedStartingVersion)
        => _inner.BuildQueryHandler(session, _unwrap(id), expectedStartingVersion);

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
        => _inner.BuildQueryHandler(session, _unwrap(id), forUpdate);

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
        => _inner.BuildQueryHandler(session, _unwrap(id));
}
