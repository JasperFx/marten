using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Fetching;

/// <summary>
/// An Inline snapshot is written in the same transaction as its events, so it is always at the stream head:
/// the version row and the stored document are everything there is to read.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: as FetchInlinedPlan. Aggregate types are preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: as FetchInlinedPlan. AOT consumers pre-generate codegen artifacts.")]
internal class FetchNaturalKeyInlinePlan<TDoc, TNaturalKey, TId>: FetchNaturalKeyPlan<TDoc, TNaturalKey, TId>
    where TDoc : class where TNaturalKey : notnull where TId : notnull
{
    public FetchNaturalKeyInlinePlan(EventGraph events, NaturalKeyDefinition naturalKey,
        NaturalKeyQueryBuilder lookup): base(events, naturalKey, lookup)
    {
    }

    public override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;

    // The document itself goes into the identity map through the IdentityOnly storage, as for
    // FetchInlinedPlan, rather than the stream.
    protected override bool RemembersStreamInIdentityMap => false;

    protected override bool UsesWriteCache => true;

    // An Inline snapshot is always exactly at the stream head, so a cached one has to be too
    protected override bool cachedSnapshotFits(long cachedVersion, long streamVersion) =>
        cachedVersion == streamVersion;

    // As FetchInlinedPlan: the commit's own projection moves the aggregate on, so the write-back is advanced to
    // the instance that projection stores
    protected override void onOwedToCache(DocumentSessionBase session, AggregateCacheKey key, TId streamId)
    {
        FetchInlinedPlan<TDoc, TId>.AdvanceAfterCommit(session, findDocumentStorage(session), key, streamId,
            IsGlobal);
    }

    protected override ValueTask<(bool found, TDoc? aggregate)> tryFetchFromIdentityMapAsync(
        DocumentSessionBase session, object keyValue, CancellationToken cancellation)
    {
        return new(tryFindInIdentityMap(session, keyValue, out var aggregate) ? (true, aggregate) : (false, null));
    }

    protected override bool tryFindInIdentityMap(DocumentSessionBase session, object keyValue,
        out TDoc? aggregate)
    {
        if (session.ItemMap.TryGetValue(typeof(TDoc), out var raw) && raw is Dictionary<TId, TDoc> map)
        {
            foreach (var candidate in map.Values)
            {
                if (hasKey(candidate, keyValue))
                {
                    aggregate = candidate;
                    return true;
                }
            }
        }

        aggregate = null;
        return false;
    }

    // The same choice of storage as a fetch by stream id
    private static IDocumentStorage<TDoc, TId> findDocumentStorage(QuerySession session)
        => FetchInlinedPlan<TDoc, TId>.findDocumentStorage(session);

    protected override void writeAggregateStatements(ICommandBuilder builder, QuerySession session,
        object keyValue, CachedSnapshot? cached)
    {
        // A cached snapshot is the whole aggregate: the snapshot load is exactly what the cache removes
        if (cached != null) return;

        builder.StartNewCommand();
        writeDocumentLoad(builder, findDocumentStorage(session), session, keyValue);
    }

    protected override async Task<(TDoc? aggregate, long version)> readAggregateAsync(DbDataReader reader, DocumentSessionBase session,
        bool found, TId streamId, object? keyValue, long version, bool detectSnapshotRace, CachedSnapshot? cached,
        CancellationToken cancellation)
    {
        if (cached != null)
        {
            // As FetchInlinedPlan on a cache hit: the identity map is what lets the inline projection reuse
            // this instance during commit
            if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(streamId, cached.Aggregate);
            }

            return (cached.Aggregate, version);
        }

        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var document = await readDocumentAsync(reader, findDocumentStorage(session), session, cancellation)
            .ConfigureAwait(false);
        return (document, version);
    }

    protected override async Task<TDoc?> applyPendingEventsAsync(DocumentSessionBase session, TId streamId,
        TDoc? aggregate, IReadOnlyList<IEvent> pending, CancellationToken cancellation)
    {
        var storage = findDocumentStorage(session);
        var raw = ((IMartenSession)session).Options.Projections.AggregatorFor<TDoc>();
        var aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                         ?? typeof(IdentityForwardingAggregator<,,,>)
                             .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, storage, typeof(TDoc),
                                 storage.IdType, typeof(TId), typeof(IQuerySession));

        aggregate = await aggregator.BuildAsync(pending, session, aggregate, streamId, storage, cancellation)
            .ConfigureAwait(false);

        // As FetchInlinedPlan.ProjectLatest: BuildAsync may have mutated the cached instance in place, and the
        // inline projection would re-apply the same events to it during SaveChangesAsync.
        session.EjectAggregateFromIdentityMap<TDoc, TId>(streamId);

        return aggregate;
    }
}
