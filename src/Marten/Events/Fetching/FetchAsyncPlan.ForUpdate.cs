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
    ///     How many times a fetch will redo itself after its snapshot went backwards between its reads, as
    ///     it does while a projection is rebuilt. Two is enough for a race: each redo reads afresh, and the
    ///     snapshot has to move backwards inside the window AGAIN to lose a second one. A violation that
    ///     survives both redos is not a race but a standing inconsistency -- the archived-partition
    ///     stream-id reuse that <c>EnableStrictStreamIdentityEnforcement</c> exists to refuse -- and the
    ///     last attempt deliberately runs with detection off so that case keeps today's behaviour instead
    ///     of becoming a new exception in somebody's production.
    /// </summary>
    private const int SnapshotRaceRedos = 2;

    /// <summary>
    ///     Runs <paramref name="fetch" /> with detection on, redoing it after a
    ///     <see cref="SnapshotRaceException" />, and once more with detection off when the redos run out.
    ///     See <see cref="SnapshotRaceRedos" />.
    /// </summary>
    private static async Task<T> withSnapshotRaceRedos<T>(DocumentSessionBase session, TId id,
        Func<bool, Task<T>> fetch)
    {
        for (var attempt = 0; attempt < SnapshotRaceRedos; attempt++)
        {
            try
            {
                return await fetch(true).ConfigureAwait(false);
            }
            catch (SnapshotRaceException)
            {
                forgetSnapshot(session, id);
            }
        }

        return await fetch(false).ConfigureAwait(false);
    }

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
                // The snapshot went backwards between this batch's two reads, as it does while a projection
                // is rebuilt. Nothing is wrong with the stream; the read just straddled the write. Go round
                // again -- but the redo has to forget what the first attempt learned first, or a snapshot that
                // has since gone is judged by the revision of the one that was there.
                forgetSnapshot(session, id);
            }
        }

        // Out of redos. Run once more with detection off so this always returns a stream rather than
        // throwing something no previous release ever threw. See SnapshotRaceRedos.
        return await fetchForWriting(session, id, forUpdate, true, false, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    ///     Drops this stream's snapshot from the session's revision tracker, so that a redo judges only what
    ///     it reads itself.
    /// </summary>
    /// <remarks>
    ///     Only the revision. The plan reads with <c>DocumentTracking.None</c> (see <see cref="AsyncFetchPlanner" />),
    ///     so a redo goes back to the row regardless, and whatever is in the session's identity map for this id
    ///     is the caller's own document rather than anything this fetch put there.
    /// </remarks>
    private static void forgetSnapshot(DocumentSessionBase session, TId id)
    {
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
        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        writeReads(builder, session, id, forUpdate, loadHandler, cacheHit, cachedVersion);

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
            // A sentinel for FetchForWriting's redo loop, not a database failure. It must not be
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
    ///     The stream version, then the events, then the snapshot document -- in that order, which is what
    ///     makes the reads safe without a shared transaction. See <see cref="writeEventFetchStatement" />.
    ///     On a cache hit the snapshot load is exactly the round trip we are here to skip.
    /// </summary>
    private void writeReads(ICommandBuilder builder, IStorageSession session, TId id, bool forUpdate,
        LoadByIdHandler<TDoc, TId> loadHandler, bool cacheHit = false, long cachedVersion = 0)
    {
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();

        if (cacheHit)
        {
            writeCachedEventFetchStatement(id, cachedVersion, builder);
            return;
        }

        writeEventFetchStatement(id, builder);

        builder.StartNewCommand();

        loadHandler.ConfigureCommand(builder, session);
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
        if (_initialSql != null) return;

        var fields = selector.SelectFields();
        _eventFieldCount = fields.Length;
        _selectedEventFields = fields.Select(x => "d." + x).Join(", ");
        _initialSql = $"select {_selectedEventFields} from {_events.DatabaseSchemaName}.mt_events as d";
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
            IReadOnlyList<IEvent> events;
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

                // Read in any events from after the cached state of the aggregate
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            }
            else
            {
                // The events after the snapshot, then the snapshot itself -- if any!
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                var delta = await readDeltaAsync(reader, selector, cancellation).ConfigureAwait(false);

                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

                var snapshotVersion = snapshotVersionFor(session, id);
                events = eventsAfterSnapshot(id, document != null, snapshotVersion, delta, detectSnapshotRace);
                version = reachedVersion(version, document != null, snapshotVersion, events);
            }

            _events.Options.OpenTelemetry
                .RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.AsyncPlan);

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

        return new ForUpdateQueryHandler(this, id, forUpdate);
    }

    public class ForUpdateQueryHandler: IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        public ForUpdateQueryHandler(FetchAsyncPlan<TDoc,TId> parent, TId id, bool forUpdate)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;
            _loadHandler = new LoadByIdHandler<TDoc, TId>(parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent.writeReads(builder, session, _id, _forUpdate, _loadHandler);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            // Detection ON, and a snapshot that went backwards surfaces as SnapshotRaceException rather than
            // being retried. A batch handler is handed one reader and cannot issue another statement, so
            // there is no redo available here -- the choice is a wrong aggregate or an exception, and for a
            // fetch whose whole purpose is to decide what to write, the exception is the better answer.
            return _parent.ReadIntoStream(documentSessionBase, _id, token, reader, _loadHandler,
                documentSessionBase.EventStorage(), detectSnapshotRace: true);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
