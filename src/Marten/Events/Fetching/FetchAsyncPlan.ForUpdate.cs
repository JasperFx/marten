using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{

    /// <summary>
    ///     How many times a fetch will redo itself after catching the async daemon mid-write. Two is
    ///     enough for a race: each redo reads a fresh snapshot, and the daemon has to commit inside the
    ///     window AGAIN to lose a second one. A violation that survives both redos is not a race but a
    ///     standing inconsistency -- the archived-partition stream-id reuse that
    ///     <c>EnableStrictStreamIdentityEnforcement</c> exists to refuse -- and the last attempt
    ///     deliberately runs with detection off so that case keeps today's behaviour instead of becoming a
    ///     new exception in somebody's production.
    /// </summary>
    private const int SnapshotRaceRedos = 2;

    [MemberNotNull(nameof(_initialSql))]
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate, CancellationToken cancellation = default)
    {
        for (var attempt = 0; attempt < SnapshotRaceRedos; attempt++)
        {
            try
            {
                return await fetchForWriting(session, id, forUpdate, true, true, cancellation).ConfigureAwait(false);
            }
            catch (CacheAheadOfDatabaseException)
            {
                // The cached snapshot claimed a higher version than the stream actually has, so the delta
                // query could not possibly reconstitute it. TryTake already removed the entry; redo the fetch
                // on the normal, always-correct uncached path -- but still write the result back, so the cache
                // heals in one round instead of two. Rare enough to not be worth optimizing further.
                return await fetchForWriting(session, id, forUpdate, false, true, cancellation).ConfigureAwait(false);
            }
            catch (SnapshotRaceException)
            {
                // #5613. The daemon committed a snapshot between this batch's two reads. Nothing is wrong
                // with the stream; the read just straddled the write. Go round again -- but the redo has to
                // forget what the first attempt learned first, or it reads the same stale snapshot straight
                // back out of the session.
                forgetSnapshot(session, id);
            }
        }

        // Out of redos. Run once more with detection off so this always returns a stream rather than
        // throwing something no previous release ever threw. See SnapshotRaceRedos.
        return await fetchForWriting(session, id, forUpdate, true, false, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    ///     Drops this stream's snapshot from the session's identity map and revision tracker so that a redo
    ///     actually goes back to the database for it.
    /// </summary>
    /// <remarks>
    ///     This plan resolves its storage with <c>DocumentTracking.IdentityOnly</c>, so the selector behind
    ///     <see cref="LoadByIdHandler{T,TId}" /> is an identity-map selector: a second load of the same id in
    ///     the same session hands back the FIRST instance and never reads the row again. Without this a redo
    ///     is guaranteed to observe exactly the stale state that triggered it, the race detector fires again,
    ///     and the retry budget is spent discovering nothing. Scoped to the one id -- EjectAllOfType would
    ///     also throw away other streams' identity and any pending work queued against the type.
    /// </remarks>
    private static void forgetSnapshot(DocumentSessionBase session, TId id)
    {
        if (session.ItemMap.TryGetValue(typeof(TDoc), out var raw) && raw is Dictionary<TId, TDoc> map)
        {
            map.Remove(id);
        }

        ((IMartenSession)session).Versions.RevisionsFor<TDoc, TId>().Remove(id);
    }

    [MemberNotNull(nameof(_initialSql))]
    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        bool useCachedSnapshot, bool detectSnapshotRace, CancellationToken cancellation)
    {
        var cache = _cache;

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        ensureInitialSql(selector);

        var cacheKey = cache == null ? default : cacheKeyFor(session, id);
        object? cachedAggregate = null;
        var cachedVersion = 0L;
        var cacheHit = cache != null && useCachedSnapshot &&
                       cache.TryTake(cacheKey, out cachedAggregate, out cachedVersion);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};

        // The exclusive path has just opened the session's own transaction above and holds a row lock in
        // it, so it must not bracket anything; everyone else shares one snapshot unless the session is
        // already inside a transaction this fetch would be committing. See canShareOneSnapshot.
        var sharedSnapshot = !forUpdate && beginSharedSnapshot(builder, session);

        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();

        // On a cache hit the snapshot load is exactly the round trip we are here to skip
        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        if (!cacheHit)
        {
            loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            writeEventFetchStatement(id, builder);
        }
        else
        {
            writeCachedEventFetchStatement(id, cachedVersion, builder);
        }

        endSharedSnapshot(builder, sharedSnapshot);

        var batch = builder.Compile();
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, loadHandler, selector,
                new CacheAttempt(cache, cacheKey, cacheHit, cachedAggregate, cachedVersion),
                detectSnapshotRace).ConfigureAwait(false);
        }
        catch (CacheAheadOfDatabaseException)
        {
            throw;
        }
        catch (SnapshotRaceException)
        {
            // #5613: a sentinel for FetchForWriting's redo loop, not a database failure. It must not be
            // reshaped into StreamLockedException by the handler below.
            throw;
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    /// <summary>
    ///     #5613. Whether the snapshot document this fetch loaded and the delta query that ran beside it
    ///     were looking at the same state of the database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The delta query finds its floor by re-reading <c>a.mt_version</c> from the aggregate table.
    ///         When the three reads share one snapshot (#5604) that is fine, but the exclusive path cannot
    ///         bracket -- it already owns a transaction, and its <c>for update</c> is on the
    ///         <c>mt_streams</c> row, which the async daemon never touches (the daemon writes the snapshot
    ///         table and <c>mt_event_progression</c>). So a daemon snapshot write landing between the two
    ///         reads leaves the delta query excluding the very events the document read never saw. Same
    ///         exposure, for the same reason, on a non-exclusive fetch whose bracket #5611 suppressed
    ///         because the session is inside a caller-owned transaction.
    ///     </para>
    ///     <para>
    ///         Detected rather than prevented, from data already in hand: the snapshot row's
    ///         <c>mt_version</c> is captured into the session's revision tracker by the very selector
    ///         <see cref="LoadByIdHandler{T,TId}" /> builds, so it costs no column and no round trip.
    ///     </para>
    ///     <para>
    ///         <b>Only the empty-delta case is checked, and that is deliberate.</b> The tempting stronger
    ///         assertion -- that a non-empty delta starts at the snapshot's version plus one -- is FALSE on
    ///         healthy data: <c>CompactStreamsAsync</c> hard-deletes event rows without renumbering
    ///         <c>version</c>, so a stream compacted at 1000 legitimately hands a fresh or lagging
    ///         projection a delta beginning at 1000. Asserting it would fire on every compacted stream, and
    ///         compaction is documented as safe for running async projections. The cost of leaving it out
    ///         is that a race caught mid-catch-up -- daemon advanced the snapshot only part of the way, so
    ///         some events still come back -- goes undetected. The full-catch-up case, which is what
    ///         actually happens when the daemon is running, always lands here with an empty delta.
    ///     </para>
    /// </remarks>
    private static bool snapshotAndDeltaDisagree(DocumentSessionBase session, TId id, long version,
        TDoc? document, IReadOnlyList<IEvent> events)
    {
        // A delta with events in it tells us nothing we can safely act on -- see the remarks above.
        if (events.Count > 0) return false;

        // Nothing appended yet, so there is nothing for a snapshot to be behind.
        if (version <= 0) return false;

        // No snapshot row at all. Then the delta query's join matched `a.mt_version is NULL` and should
        // have returned EVERY event of this stream, so an empty delta is flatly impossible from the state
        // the document read saw -- the floor came from a snapshot the daemon committed in between. This is
        // the FIRST-snapshot case, and the loudest one: the aggregate comes back null for a live stream.
        if (document == null) return true;

        // A row was read but no revision came with it. Nothing to compare, and inventing a race from an
        // absent measurement is how a guard like this turns into spurious retries.
        var snapshotRevision = ((IMartenSession)session).Versions.RevisionFor<TDoc, TId>(id);
        if (snapshotRevision == null) return false;

        // The delta is empty, so the snapshot is supposed to already be at the stream's head. If it is
        // behind, the floor the delta query used came from a NEWER snapshot than the one we hold, and the
        // events in between have been dropped from both.
        return snapshotRevision.Value < version;
    }

    /// <summary>
    ///     Everything the read side needs to know about a cache attempt. Default value == caching off.
    /// </summary>
    private readonly record struct CacheAttempt(
        IAggregateWriteCache? Cache,
        AggregateCacheKey Key,
        bool Hit,
        object? Aggregate,
        long Version);

    /// <summary>
    ///     Signals that a cached snapshot was ahead of the database and the fetch must be redone uncached.
    ///     Never escapes <see cref="FetchForWriting" />.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="MartenException" /> even though it is private and never observable,
    ///     because <c>all_exceptions_should_derive_from_MartenException</c> is a whole-assembly convention
    ///     rather than a rule about the public surface — and a convention with a nested-private carve-out
    ///     stops being one.
    /// </remarks>
    private sealed class CacheAheadOfDatabaseException: MartenException;

    private void ensureInitialSql(IEventStorage selector)
    {
        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";
    }

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, CancellationToken cancellation,
        DbDataReader reader, LoadByIdHandler<TDoc, TId> loadHandler, IEventStorage selector,
        CacheAttempt cacheAttempt = default, bool detectSnapshotRace = false)
    {
        long version = 0;
        try
        {
            // Read the latest version
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            TDoc? document;
            if (cacheAttempt.Hit)
            {
                if (cacheAttempt.Version > version)
                {
                    // The cache is ahead of the database -- a restore, a rollback, or a key collision bug.
                    // The snapshot was never fetched in this batch so there is nothing to recover from here.
                    // Drain the batch so the connection is left clean, then let FetchForWriting retry uncached.
                    while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
                    {
                    }

                    throw new CacheAheadOfDatabaseException();
                }

                document = (TDoc)cacheAttempt.Aggregate!;
            }
            else
            {
                // Fetch the existing aggregate -- if any!
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            }

            // Read in any events from after the current state of the aggregate
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            _events.Options.OpenTelemetry
                .RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.AsyncPlan);

            if (detectSnapshotRace && !cacheAttempt.Hit
                                   && snapshotAndDeltaDisagree(session, id, version, document, events))
            {
                throw new SnapshotRaceException(typeof(TDoc), id);
            }

            if (events.Any())
            {
                document = await _aggregator.BuildAsync(events, session, document, id, _storage, cancellation).ConfigureAwait(false);
            }

            if (document != null)
            {
                _storage.SetIdentity(document, id);
            }

            // The aggregate is now at the stream version, which is durable truth regardless of what the
            // caller does with the stream next. Under the Async lifecycle any events the caller appends are
            // not applied to this instance in session, so it cannot drift ahead of the database.
            if (cacheAttempt.Cache != null && document != null && version > 0)
            {
                cacheAttempt.Cache.Store(cacheAttempt.Key, document, version);
            }

            var stream = version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);

            // This is an optimization for calling FetchForWriting, then immediately calling FetchLatest
            if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, stream);
            }

            return stream;
        }
        catch (CacheAheadOfDatabaseException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException!);
            }

            throw;
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        var dsb = session.AssertIsDocumentSession();
        if (_initialSql.IsEmpty())
        {
            ensureInitialSql(dsb.EventStorage());
        }

        // #5611: decided here, where the session is in hand, rather than inside ConfigureCommand -- the
        // batch has to know the answer at ENLISTMENT time, before any SQL is built, because
        // OpensItsOwnTransaction is what gates the must-be-first and no-mixing-with-exclusive rules.
        return new ForUpdateQueryHandler(this, id, forUpdate, !forUpdate && canShareOneSnapshot(dsb));
    }

    public class ForUpdateQueryHandler: IQueryHandler<IEventStream<TDoc>>, IOpensItsOwnTransaction
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly bool _sharedSnapshot;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        /// <summary>
        ///     True exactly when <see cref="ConfigureCommand" /> below brackets its reads in
        ///     <c>begin transaction … end</c>: the non-exclusive case, on a session not already inside a
        ///     transaction of its own. See <see cref="IOpensItsOwnTransaction" /> for why a batch needs to
        ///     know, and <see cref="canShareOneSnapshot" /> for the second condition. A batch on a session
        ///     that holds its own transaction emits no bracket, so none of the rules that exist to protect
        ///     one apply to it.
        /// </summary>
        public bool OpensItsOwnTransaction => _sharedSnapshot;

        public string CallName => "FetchForWriting";

        public ForUpdateQueryHandler(FetchAsyncPlan<TDoc,TId> parent, TId id, bool forUpdate, bool sharedSnapshot)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;
            _sharedSnapshot = sharedSnapshot;
            _loadHandler = new LoadByIdHandler<TDoc, TId>(parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            // Asked again here rather than reusing _sharedSnapshot: the session can acquire a transaction
            // between enlistment and Execute(), and this is the last moment before the SQL goes out. The
            // enlistment-time answer only has to be good enough for the batch's own rules; this one has to
            // be right.
            var sharedSnapshot = !_forUpdate && beginSharedSnapshot(builder, (QuerySession)session);

            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();

            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);

            endSharedSnapshot(builder, sharedSnapshot);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            // #5613: detection ON, and it surfaces as SnapshotRaceException rather than being retried. A
            // batch handler is handed one reader and cannot issue another statement, so there is no redo
            // available here -- the choice is a wrong aggregate or an exception, and for a fetch whose whole
            // purpose is to decide what to write, the exception is the better answer. Retry the batch.
            return _parent.ReadIntoStream(documentSessionBase, _id, token, reader, _loadHandler,
                documentSessionBase.EventStorage(), detectSnapshotRace: true);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
