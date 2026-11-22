using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

/// <summary>
/// Fetches an aggregate by natural key in one round trip: a JOIN from the lookup table to
/// <c>mt_streams</c> reads the stream's version and identity, and the lifecycle's own reads (the snapshot,
/// the events after it, or the whole stream) find the stream through a subselect on the same lookup. No
/// statement depends on the result of another, so the same SQL runs standalone and inside a batch query.
/// </summary>
/// <remarks>
/// <para>
/// The lookup table belongs to the aggregate that owns the key. For a mapped key
/// (<see cref="NaturalKeyAttribute.SourceProjection"/>) that is the source projection's aggregate, which
/// maintains the table inline, so an Async aggregate is found by its key as soon as its events are
/// committed, before the daemon has caught up.
/// </para>
/// <para>
/// The Inline, Async and Live subclasses mirror <see cref="FetchInlinedPlan{TDoc,TId}"/>,
/// <see cref="FetchAsyncPlan{TDoc,TId}"/> and <see cref="FetchLivePlan{TDoc,TId}"/>, including their write cache
/// and identity map short cuts. The identity map is searched for an aggregate whose current key matches, and the
/// write cache is reached through <see cref="NaturalKeyStreamIndex"/>. The key itself is still resolved in the
/// database, in the same round trip.
/// </para>
/// </remarks>
internal abstract class FetchNaturalKeyPlan<TDoc, TNaturalKey, TId>: IAggregateFetchPlan<TDoc, TNaturalKey>
    where TDoc : class where TNaturalKey : notnull where TId : notnull
{
    protected readonly EventGraph _events;
    private readonly NaturalKeyDefinition _naturalKey;
    protected readonly NaturalKeyQueryBuilder _lookup;
    private readonly IAggregateWriteCache? _cache;

    protected FetchNaturalKeyPlan(EventGraph events, NaturalKeyDefinition naturalKey, NaturalKeyQueryBuilder lookup)
    {
        _events = events;
        _naturalKey = naturalKey;
        _lookup = lookup;
        IsGlobal = events.GlobalAggregates.Contains(typeof(TDoc));

        // Resolved once per plan, as for the stream-id plans
        if (UsesWriteCache && events.AggregateWriteCaching.IsEnabled(typeof(TDoc)))
        {
            _cache = events.AggregateWriteCaching.ResolveCache(typeof(TDoc));
        }
    }

    public bool IsGlobal { get; }

    public abstract ProjectionLifecycle Lifecycle { get; }

    /// <summary>
    /// Whether a fetched stream is remembered in the identity map for a later FetchLatest by stream id, as
    /// the Async and Live stream plans do.
    /// </summary>
    protected virtual bool RemembersStreamInIdentityMap => true;

    /// <summary>
    /// Whether FetchForWriting uses the aggregate write cache, as the Inline and Async stream-id plans do.
    /// Constant per subclass: it is read from the base constructor.
    /// </summary>
    protected virtual bool UsesWriteCache => false;

    /// <summary>
    /// A snapshot claimed from the aggregate write cache through <see cref="NaturalKeyStreamIndex"/>, for the
    /// stream the natural key pointed at when the fetch began. Claimed through the session's
    /// <see cref="AggregateWriteCacheSession"/>, as the stream-id plans claim theirs.
    /// </summary>
    protected sealed record CachedSnapshot(AggregateCacheKey Key, TId StreamId, AggregateWriteBaseline<TDoc> Baseline)
    {
        public TDoc Aggregate => Baseline.Aggregate!;
        public long Version => Baseline.Version;
    }

    /// <summary>
    /// The statements after the version row, each starting with <c>builder.StartNewCommand()</c> and finding
    /// the stream through <see cref="NaturalKeyQueryBuilder.WriteStreamIdSubselect"/>. With a
    /// <paramref name="cached"/> snapshot, only what is needed on top of it.
    /// </summary>
    protected abstract void writeAggregateStatements(ICommandBuilder builder, QuerySession session, object keyValue,
        CachedSnapshot? cached);

    /// <summary>
    /// Reads the result sets <see cref="writeAggregateStatements"/> produced, moving to each with
    /// <c>NextResultAsync</c>. Reads all of them whether or not the key was found, so that a batch can move on
    /// to its next item. Returns the aggregate and the stream version it reflects, which is past
    /// <paramref name="version"/> when the reads after the version row saw events appended since.
    /// </summary>
    protected abstract Task<(TDoc? aggregate, long version)> readAggregateAsync(DbDataReader reader, DocumentSessionBase session, bool found,
        TId streamId, object? keyValue, long version, bool detectSnapshotRace, CachedSnapshot? cached, CancellationToken cancellation);

    /// <summary>
    /// Folds this session's pending events for the stream into the aggregate, for <see cref="ProjectLatest"/>.
    /// </summary>
    protected abstract Task<TDoc?> applyPendingEventsAsync(DocumentSessionBase session, TId streamId,
        TDoc? aggregate, IReadOnlyList<IEvent> pending, CancellationToken cancellation);

    /// <summary>
    /// Called before the Async plan's redo after a <see cref="SnapshotRaceException"/>, to forget what the
    /// first attempt loaded.
    /// </summary>
    protected virtual void forgetSnapshot(DocumentSessionBase session, TId streamId)
    {
    }

    /// <summary>
    /// Whether a cached snapshot at <paramref name="cachedVersion"/> can be the baseline for a stream now at
    /// <paramref name="streamVersion"/>.
    /// </summary>
    protected virtual bool cachedSnapshotFits(long cachedVersion, long streamVersion) => false;

    /// <summary>
    /// Called once the fetched aggregate is owed back to the write cache, for a lifecycle whose commit moves the
    /// aggregate on. The Inline plan hands the write-back the instance its projection stores, as
    /// <see cref="FetchInlinedPlan{TDoc,TId}"/> does.
    /// </summary>
    protected virtual void onOwedToCache(DocumentSessionBase session, AggregateCacheKey key, TId streamId)
    {
    }

    /// <summary>
    /// The aggregate for this key in the session's identity map, when there is one: found by its current key
    /// value rather than registered under it, so whatever removes the entry by stream id also removes it here.
    /// </summary>
    protected virtual ValueTask<(bool found, TDoc? aggregate)> tryFetchFromIdentityMapAsync(
        DocumentSessionBase session, object keyValue, CancellationToken cancellation)
        => new((false, default));

    /// <summary>
    /// The synchronous form of <see cref="tryFetchFromIdentityMapAsync"/>, for a batch query, where only an
    /// aggregate that needs nothing folding onto it can be handed back without a query.
    /// </summary>
    protected virtual bool tryFindInIdentityMap(DocumentSessionBase session, object keyValue, out TDoc? aggregate)
    {
        aggregate = default;
        return false;
    }

    protected bool hasKey(object candidate, object keyValue) => Equals(_naturalKey.ReadValue(candidate), keyValue);

    /// <summary>
    /// Whether a stream fetched earlier in the session has this key once its appended events are counted. Worked
    /// out from the key's event mappings rather than by folding the events: folding can mutate the aggregate in
    /// place, and only the stream that matches may be folded.
    /// </summary>
    protected bool currentKeyMatches(IEventStream<TDoc> stream, object keyValue)
    {
        if (stream.Aggregate == null) return false;

        // A mapped key changes when its owner's key does, through the owner's mappings
        var owner = _naturalKey.SourceProjectionDefinition ?? _naturalKey;

        var current = _naturalKey.ReadValue(stream.Aggregate);
        foreach (var @event in stream.Events)
        {
            foreach (var mapping in owner.EventMappings)
            {
                if (mapping.EventType.IsAssignableFrom(@event.Data.GetType()))
                {
                    current = owner.Unwrap(mapping.Extractor(@event));
                }
            }
        }

        return Equals(current, keyValue);
    }

    /// <summary>
    /// Called before folding the events appended to a stream fetched earlier in the session onto its aggregate, as
    /// FetchAsyncPlan.FetchForReading and FetchLivePlan.FetchForReading do for a stream id. The fold lands on the
    /// very instance the stream holds, which then carries events its version says it does not. So the stream
    /// leaves the identity map, and the instance's write cache registration is dropped, or the next fetch would
    /// fold the same events a second time.
    /// </summary>
    protected void releaseStreamBeforeFolding(DocumentSessionBase session, TId streamId, IEventStream<TDoc> stream)
    {
        if (stream.Aggregate == null || stream.Events.Count == 0) return;

        if (_cache != null)
        {
            session.AggregateWriteCache.Discard(cacheKeyFor(session, streamId));
        }

        session.EjectAggregateFromIdentityMap<IEventStream<TDoc>, TId>(streamId);
    }

    protected virtual async Task prepareAsync(DocumentSessionBase session, CancellationToken cancellation)
    {
        // The event storage includes the natural key lookup tables
        await identityStrategy(session).EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);
    }

    protected static IEventIdentityStrategy<TId> identityStrategy(QuerySession session)
        => (IEventIdentityStrategy<TId>)session.Events;

    protected object keyValueFor(TNaturalKey id) => _naturalKey.Unwrap(id)!;

    /// <summary>
    /// A null key, or a strong-typed key whose value is null. No stream can have it, so a read finds nothing
    /// without a query and a write is refused.
    /// </summary>
    private bool isNullKey(TNaturalKey id) => id is null || _naturalKey.Unwrap(id) is null;

    private AggregateCacheKey cacheKeyFor(DocumentSessionBase session, object id)
    {
        return new AggregateCacheKey(
            typeof(TDoc),
            session.Database.Identifier,
            IsGlobal ? AggregateCacheKey.GlobalTenant : session.TenantId,
            id);
    }

    #region writing

    /// <summary>
    /// A null key is refused the way an empty Guid or a null string stream id is: there is no stream to load,
    /// and no key for a new stream to claim. That includes a strong-typed key whose value is null, which is what
    /// <c>default</c> of a struct wrapping a string is.
    /// </summary>
    private void assertKeyForWriting(TNaturalKey id)
    {
        if (isNullKey(id))
        {
            throw new ArgumentOutOfRangeException(nameof(id),
                $"Cannot use a null natural key of type {typeof(TNaturalKey).FullNameInCode()} to fetch {typeof(TDoc).FullNameInCode()} for writing");
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TNaturalKey id,
        bool forUpdate, CancellationToken cancellation = default)
    {
        assertKeyForWriting(id);
        return await withRedos(session,
            detect => fetchForWritingWithCache(session, id, forUpdate, detect, cancellation)).ConfigureAwait(false);
    }

    /// <summary>
    /// How many times a fetch is redone after <see cref="NaturalKeyMovedException"/> before the exception reaches
    /// the caller. Each redo needs the key to change hands again inside one round trip.
    /// </summary>
    private const int KeyMovedRedos = 3;

    /// <summary>
    /// Runs <paramref name="fetch"/> with snapshot race detection on, redoing it after a
    /// <see cref="SnapshotRaceException"/>, and once more with detection off when the redos run out. Only the
    /// Async plan detects the race. See FetchAsyncPlan.SnapshotRaceRedos for why two redos, and why the last
    /// attempt runs with detection off. A <see cref="NaturalKeyMovedException"/> is redone too, up to
    /// <see cref="KeyMovedRedos"/> times, and never with its detection off.
    /// </summary>
    private async Task<T> withRedos<T>(DocumentSessionBase session, Func<bool, Task<T>> fetch)
    {
        var snapshotRaces = 0;
        var keyMoves = 0;
        while (true)
        {
            var detectSnapshotRace = snapshotRaces < 2;
            try
            {
                return await fetch(detectSnapshotRace).ConfigureAwait(false);
            }
            catch (SnapshotRaceException e) when (detectSnapshotRace)
            {
                forgetSnapshot(session, (TId)e.Id);
                snapshotRaces++;
            }
            catch (NaturalKeyMovedException) when (keyMoves < KeyMovedRedos)
            {
                keyMoves++;
            }
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TNaturalKey id,
        long expectedStartingVersion, CancellationToken cancellation = default)
    {
        var stream = await FetchForWriting(session, id, false, cancellation).ConfigureAwait(false);
        assertExpectedVersion(id, expectedStartingVersion, stream.CurrentVersion ?? 0);
        return stream;
    }

    private async Task<IEventStream<TDoc>> fetchForWritingWithCache(DocumentSessionBase session, TNaturalKey id,
        bool forUpdate, bool detectSnapshotRace, CancellationToken cancellation)
    {
        try
        {
            return await fetchForWriting(session, id, forUpdate, detectSnapshotRace, true, cancellation)
                .ConfigureAwait(false);
        }
        catch (CachedSnapshotUnusableException)
        {
            // The pointer named another stream than the database did, or the snapshot did not fit the stream's
            // version. Either way the always-correct uncached path follows.
            return await fetchForWriting(session, id, forUpdate, detectSnapshotRace, false, cancellation)
                .ConfigureAwait(false);
        }
    }

    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TNaturalKey id,
        bool forUpdate, bool detectSnapshotRace, bool useCache, CancellationToken cancellation)
    {
        await prepareAsync(session, cancellation).ConfigureAwait(false);

        var keyValue = keyValueFor(id);
        var cached = useCache ? tryTakeCachedSnapshot(session, keyValue) : null;

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder { TenantId = session.TenantId };

        writeFetch(builder, session, keyValue, forUpdate, cached);

        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await readStreamAsync(reader, session, keyValue, detectSnapshotRace, cached, cancellation)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not SnapshotRaceException && e is not CachedSnapshotUnusableException &&
                                  isLockFailure(e))
        {
            throw new StreamLockedException(id, e.InnerException!);
        }
    }

    private CachedSnapshot? tryTakeCachedSnapshot(DocumentSessionBase session, object? keyValue)
    {
        if (_cache == null || keyValue == null) return null;

        var scope = cacheKeyFor(session, keyValue);
        if (!_events.NaturalKeyStreams.TryFind(_naturalKey, scope, keyValue, out var pointed) ||
            pointed is not TId streamId)
        {
            return null;
        }

        var key = cacheKeyFor(session, streamId);
        var baseline = session.AggregateWriteCache.TakeBaseline<TDoc>(_cache, key);
        return baseline.HasAggregate ? new CachedSnapshot(key, streamId, baseline) : null;
    }

    private async Task<IEventStream<TDoc>> readStreamAsync(DbDataReader reader, DocumentSessionBase session,
        object? keyValue, bool detectSnapshotRace, CachedSnapshot? cached, CancellationToken cancellation)
    {
        var (found, streamId, version, aggregate) =
            await readFetchAsync(reader, session, keyValue, detectSnapshotRace, cached, cancellation)
                .ConfigureAwait(false);

        var identity = identityStrategy(session);

        // No live stream has this key, so this is a new stream. Its key is claimed when the projection that
        // owns the key sees the first event that sets it.
        if (!found) return identity.StartStream<TDoc>(null, session, newStreamId(), cancellation);

        // jasperfx#1050: owed to the cache at the version the aggregate reflects, and written back only after a
        // commit this session owns. Without a cached snapshot for this very stream, the claim is made now, which
        // also clears an entry the stream has moved past.
        if (_cache != null && aggregate != null)
        {
            var key = cacheKeyFor(session, streamId);
            var baseline = cached != null && cached.Key.Equals(key)
                ? cached.Baseline
                : session.AggregateWriteCache.TakeBaseline<TDoc>(_cache, key, version);

            session.AggregateWriteCache.Register(baseline, aggregate, version);
            onOwedToCache(session, key, streamId);
        }

        var stream = version == 0
            ? identity.StartStream(aggregate, session, streamId, cancellation)
            : identity.AppendToStream(aggregate, session, streamId, version, cancellation);

        if (RemembersStreamInIdentityMap && ((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            session.StoreDocumentInItemMap(streamId, stream);
        }

        return stream;
    }

    private static void assertExpectedVersion(TNaturalKey id, long expected, long actual)
    {
        if (expected != actual)
        {
            throw new ConcurrencyException(
                $"Expected the existing version to be {expected}, but was {actual}",
                typeof(TDoc), id);
        }
    }

    private static bool isLockFailure(Exception e)
    {
        return e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction } ||
               (e.InnerException != null && e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage));
    }

    private static TId newStreamId()
    {
        return typeof(TId) == typeof(Guid)
            ? (TId)(object)SequentialGuid.NextPostgresOrder()
            : (TId)(object)SequentialGuid.NextPostgresOrder().ToString();
    }

    /// <summary>
    /// Signals that a snapshot taken from the cache cannot be used and the fetch must be redone uncached.
    /// Never escapes <see cref="FetchForWriting(DocumentSessionBase,TNaturalKey,bool,CancellationToken)"/>.
    /// Derives from <see cref="MartenException"/> for the assembly-wide convention, as
    /// <c>FetchAsyncPlan.CacheAheadOfDatabaseException</c> does.
    /// </summary>
    private sealed class CachedSnapshotUnusableException: MartenException;

    #endregion

    #region reading

    public async ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TNaturalKey id,
        CancellationToken cancellation)
    {
        var keyValue = keyValueFor(id);
        if (keyValue != null && ((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            var (inMap, fromMap) = await tryFetchFromIdentityMapAsync(session, keyValue, cancellation)
                .ConfigureAwait(false);
            if (inMap) return fromMap;
        }

        var (found, _, _, aggregate) = await withRedos(session,
            detect => fetchForReading(session, id, detect, cancellation)).ConfigureAwait(false);
        return found ? aggregate : default;
    }

    public async ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TNaturalKey id,
        CancellationToken cancellation)
    {
        // Accepted behaviour: the key is resolved against the committed lookup, not this session's pending
        // events. A stream started in this session has no lookup row until SaveChanges runs the projection
        // that writes it, so it cannot be found here. A committed stream can, and this session's pending
        // events for it are folded in, even when they change the key: the old key still finds the stream,
        // and returns the aggregate under its new key, while the new key finds nothing until the commit.
        // Not exposed publicly: there is no ProjectLatest<T, TId> on IEventStoreOperations.
        var (found, streamId, _, aggregate) = await withRedos(session,
            detect => fetchForReading(session, id, detect, cancellation)).ConfigureAwait(false);
        if (!found) return default;

        var pending = FetchPlanHelper.FindPendingEvents<TId>(session, streamId);
        if (pending is not { Count: > 0 }) return aggregate;

        return await applyPendingEventsAsync(session, streamId, aggregate, pending, cancellation)
            .ConfigureAwait(false);
    }

    // StreamLatestJson only takes a stream id, so nothing streams an aggregate's JSON by natural key
    public Task<bool> StreamForReading(DocumentSessionBase session, TNaturalKey id, Stream destination,
        CancellationToken cancellation)
        => throw new NotSupportedException(
            $"Streaming {typeof(TDoc).FullNameInCode()} as JSON by natural key is not supported. Use StreamLatestJson with the stream id.");

    private async Task<(bool found, TId streamId, long version, TDoc? aggregate)> fetchForReading(
        DocumentSessionBase session, TNaturalKey id, bool detectSnapshotRace, CancellationToken cancellation)
    {
        if (isNullKey(id)) return (false, default!, 0, default);

        await prepareAsync(session, cancellation).ConfigureAwait(false);

        var keyValue = keyValueFor(id);
        var builder = new BatchBuilder { TenantId = session.TenantId };
        writeFetch(builder, session, keyValue, false, null);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

        return await readFetchAsync(reader, session, keyValue, detectSnapshotRace, null, cancellation)
            .ConfigureAwait(false);
    }

    #endregion

    #region the one batched command

    private void writeFetch(ICommandBuilder builder, QuerySession session, object keyValue, bool forUpdate,
        CachedSnapshot? cached)
    {
        _lookup.WriteVersionAndStreamId(builder, keyValue, forUpdate);

        writeAggregateStatements(builder, session, keyValue, cached);

        // Every statement resolves the key for itself, so the key is resolved once more after the aggregate's
        // reads: when it still names the stream the first statement found, so did every read in between
        builder.StartNewCommand();
        _lookup.WriteStreamId(builder, keyValue);
    }

    private async Task<(bool found, TId streamId, long version, TDoc? aggregate)> readFetchAsync(
        DbDataReader reader, DocumentSessionBase session, object? keyValue, bool detectSnapshotRace,
        CachedSnapshot? cached, CancellationToken cancellation)
    {
        var found = false;
        var version = 0L;
        TId streamId = default!;

        if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
        {
            found = true;
            version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            streamId = await reader.GetFieldValueAsync<TId>(1, cancellation).ConfigureAwait(false);
        }

        if (cached != null)
        {
            await assertCachedSnapshotIsUsableAsync(reader, session, found, streamId, version, keyValue!, cached,
                    cancellation)
                .ConfigureAwait(false);
        }

        // A cached snapshot is never checked for a race, as on the stream-id plans: it was not read beside a delta
        var (aggregate, reached) = await readAggregateAsync(reader, session, found, streamId, keyValue, version,
            detectSnapshotRace && cached == null, cached, cancellation).ConfigureAwait(false);

        await assertKeyStillNamesTheStreamAsync(reader, found, streamId, keyValue, cancellation).ConfigureAwait(false);

        return (found, streamId, reached, aggregate);
    }

    /// <summary>
    /// Reads the last statement of the fetch, which resolves the key again. A key that no longer names the stream
    /// the first statement found changed hands while the fetch ran, and the reads in between may describe another
    /// stream, or none.
    /// </summary>
    private async Task assertKeyStillNamesTheStreamAsync(DbDataReader reader, bool found, TId streamId,
        object? keyValue, CancellationToken cancellation)
    {
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);

        // A key that named no stream at the start is answered as a new stream, whatever the reads after it saw:
        // nothing is read for a stream that was not found
        if (!found) return;

        var unchanged = await reader.ReadAsync(cancellation).ConfigureAwait(false) &&
                        EqualityComparer<TId>.Default.Equals(
                            await reader.GetFieldValueAsync<TId>(0, cancellation).ConfigureAwait(false), streamId);

        if (!unchanged) throw new NaturalKeyMovedException(typeof(TDoc), keyValue!);
    }

    /// <summary>
    /// The database, not the pointer, says which stream has the key. Only checked on the standalone path,
    /// where the reader belongs to this fetch alone and can be drained before the redo.
    /// </summary>
    private async Task assertCachedSnapshotIsUsableAsync(DbDataReader reader, DocumentSessionBase session,
        bool found, TId streamId, long version, object keyValue, CachedSnapshot cached,
        CancellationToken cancellation)
    {
        if (found && EqualityComparer<TId>.Default.Equals(streamId, cached.StreamId))
        {
            if (cachedSnapshotFits(cached.Version, version)) return;

            // Wrong for its own stream, so it is not owed back. The claim has already removed it.
            await drainAsync(reader, cancellation).ConfigureAwait(false);
            throw new CachedSnapshotUnusableException();
        }

        // The key has moved, or gone. The snapshot is still right for its own stream and was never handed to anyone,
        // so it goes straight back, unless a newer one has been stored meanwhile, and the stale pointer is dropped.
        _cache!.StoreIfNewer(cached.Key, cached.Aggregate, cached.Version);
        _events.NaturalKeyStreams.Forget(_naturalKey, cached.Key, keyValue);

        await drainAsync(reader, cancellation).ConfigureAwait(false);
        throw new CachedSnapshotUnusableException();
    }

    private static async Task drainAsync(DbDataReader reader, CancellationToken cancellation)
    {
        // Leave the connection clean for the redo
        while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
        {
        }
    }

    #endregion

    #region batch query handlers

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TNaturalKey id,
        long expectedStartingVersion)
    {
        session.AssertIsDocumentSession();
        assertKeyForWriting(id);
        return new StreamHandler(this, id, false, expectedStartingVersion);
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TNaturalKey id,
        bool forUpdate)
    {
        session.AssertIsDocumentSession();
        assertKeyForWriting(id);
        return new StreamHandler(this, id, forUpdate, null);
    }

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TNaturalKey id)
    {
        if (isNullKey(id)) return new PreCannedQueryHandler<TDoc?>(default);

        // As FetchInlinedPlan does for a stream id: an aggregate already in the identity map needs no query
        if (session is DocumentSessionBase dsb && dsb.Options.Events.UseIdentityMapForAggregates &&
            keyValueFor(id) is { } keyValue && tryFindInIdentityMap(dsb, keyValue, out var inMap))
        {
            return new PreCannedQueryHandler<TDoc?>(inMap);
        }

        return new LatestHandler(this, id);
    }

    private sealed class StreamHandler: IQueryHandler<IEventStream<TDoc>>, IExclusiveFetchHandler
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey, TId> _parent;
        private readonly TNaturalKey _id;
        private readonly bool _forUpdate;
        private readonly long? _expectedVersion;

        public StreamHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey, TId> parent, TNaturalKey id, bool forUpdate,
            long? expectedVersion)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;
            _expectedVersion = expectedVersion;
        }

        // The version read that takes the lock is the first statement writeFetch writes
        public object? ExclusiveStreamId => _forUpdate ? _id : null;

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent.writeFetch(builder, (QuerySession)session, _parent.keyValueFor(_id), _forUpdate, null);
        }

        public async Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session,
            CancellationToken token)
        {
            IEventStream<TDoc> stream;
            try
            {
                // A batch cannot redo itself, so a snapshot race surfaces as SnapshotRaceException, as it does
                // for a batched fetch by stream id.
                stream = await _parent.readStreamAsync(reader, (DocumentSessionBase)session,
                    _parent.keyValueFor(_id), true, null, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not SnapshotRaceException && isLockFailure(e))
            {
                throw new StreamLockedException(_id, e.InnerException!);
            }

            if (_expectedVersion.HasValue)
            {
                assertExpectedVersion(_id, _expectedVersion.Value, stream.CurrentVersion ?? 0);
            }

            return stream;
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class LatestHandler: IQueryHandler<TDoc?>
    {
        private readonly FetchNaturalKeyPlan<TDoc, TNaturalKey, TId> _parent;
        private readonly TNaturalKey _id;

        public LatestHandler(FetchNaturalKeyPlan<TDoc, TNaturalKey, TId> parent, TNaturalKey id)
        {
            _parent = parent;
            _id = id;
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent.writeFetch(builder, (QuerySession)session, _parent.keyValueFor(_id), false, null);
        }

        public async Task<TDoc?> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            // A batch cannot redo itself, so a snapshot race surfaces as SnapshotRaceException, as in
            // StreamHandler
            var (found, _, _, aggregate) = await _parent
                .readFetchAsync(reader, (DocumentSessionBase)session, _parent.keyValueFor(_id), true, null, token)
                .ConfigureAwait(false);

            return found ? aggregate : default;
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

    #endregion

    #region shared statements

    /// <summary>
    /// <c>select [document fields] where id = (the key's stream id)</c>, for the storage's own selector.
    /// </summary>
    protected void writeDocumentLoad(ICommandBuilder builder, IDocumentStorage<TDoc, TId> storage,
        IStorageSession session, object keyValue)
    {
        builder.Append("select ");

        var fields = storage.SelectFields();
        builder.Append(fields[0]);
        for (var i = 1; i < fields.Length; i++)
        {
            builder.Append(", ");
            builder.Append(fields[i]);
        }

        builder.Append(" from ");
        builder.Append(storage.FromObject);
        builder.Append(" as d where id = ");
        _lookup.WriteStreamIdSubselect(builder, keyValue);

        storage.AddTenancyFilter(builder, session.TenantId);
    }

    /// <summary>
    /// Reads what <see cref="writeDocumentLoad"/> selected, through <see cref="LoadByIdHandler{T,TId}"/>,
    /// which uses its id only to write SQL.
    /// </summary>
    protected static Task<TDoc> readDocumentAsync(DbDataReader reader, IDocumentStorage<TDoc, TId> storage,
        DocumentSessionBase session, CancellationToken cancellation)
    {
        return new LoadByIdHandler<TDoc, TId>(storage, default!).HandleAsync(reader, session, cancellation);
    }

    #endregion
}
