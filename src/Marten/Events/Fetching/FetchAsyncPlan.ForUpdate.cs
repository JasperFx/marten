using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
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
    private async Task<T> withSnapshotRaceRedos<T>(DocumentSessionBase session, TId id,
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
    private void forgetSnapshot(DocumentSessionBase session, TId id) => forgetSnapshot(_storage, session, id);

    internal static void forgetSnapshot(IDocumentStorage<TDoc, TId> storage, DocumentSessionBase session, TId id)
    {
        if (storage is IValueTypeRevisions<TId> wrapped)
        {
            wrapped.ClearRevision(session, id);
        }
        else
        {
            ((IMartenSession)session).Versions.ClearRevision<TDoc, TId>(id);
        }
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

        // jasperfx#1050: the claim goes through the session's AggregateWriteCacheSession, which refuses a
        // snapshot at version zero or of the wrong type, and owes the aggregate back only after a commit
        // this session owns. A redo after an unusable snapshot gives up the snapshot but keeps the slot,
        // so the uncached fetch still writes back.
        var baseline = cache == null
            ? default
            : session.AggregateWriteCache.TakeBaseline<TDoc>(cache, cacheKeyFor(session, id));
        if (!useCachedSnapshot) baseline = baseline.WithoutAggregate();

        var cacheHit = baseline.HasAggregate;
        var cachedVersion = baseline.Version;

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
                baseline, detectSnapshotRace).ConfigureAwait(false);
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
        AggregateWriteBaseline<TDoc> cacheAttempt = default, bool detectSnapshotRace = false,
        long? expectedStartingVersion = null)
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
            if (cacheAttempt.HasAggregate)
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

                document = cacheAttempt.Aggregate;

                // Read in any events from after the cached state of the aggregate
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);

                // An event appended between the two reads is in the events, so the aggregate is at its version
                version = reachedVersion(version, false, null, events);
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

            // The stream says there are events past the snapshot and the delta returned none: they were
            // archived out from under it, and folding nothing would hand back a stale aggregate
            if (cacheAttempt.NeedsArchivalRefetch(events, version))
            {
                while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
                {
                }

                throw new CacheAheadOfDatabaseException();
            }

            return await completeStream(session, id, document, events, version, cacheAttempt,
                expectedStartingVersion, cancellation).ConfigureAwait(false);
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

    /// <summary>
    ///     Checks the expected version, folds the events onto the document, owes the result to the cache, and starts
    ///     or appends to the stream.
    /// </summary>
    /// <param name="version">
    ///     The version the aggregate reached, which is past the stream version read first if events were appended in
    ///     between. An expected version is compared with this one.
    /// </param>
    private async Task<IEventStream<TDoc>> completeStream(DocumentSessionBase session, TId id, TDoc? document,
        IReadOnlyList<IEvent> events, long version, AggregateWriteBaseline<TDoc> baseline,
        long? expectedStartingVersion, CancellationToken cancellation)
    {
        if (expectedStartingVersion.HasValue && expectedStartingVersion.Value != version)
        {
            throw new ConcurrencyException(
                $"Expected the existing version to be {expectedStartingVersion.Value}, but was {version}",
                typeof(TDoc), id);
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

        // The aggregate is now at the stream version. Under the Async lifecycle the events the caller
        // appends are not applied to this instance in session, so that label stays true through the
        // commit. jasperfx#1050: owed to the cache, not stored in it. Storing at fetch time put an
        // instance the caller was still reading where a second session could claim it and fold onto
        // it, and left it there when the caller's commit failed.
        session.AggregateWriteCache.Register(baseline, document, version);

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

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        var dsb = session.AssertIsDocumentSession();
        if (_initialSql.IsEmpty())
        {
            ensureInitialSql(dsb.EventStorage());
        }

        // Detection ON, and a snapshot that went backwards surfaces as SnapshotRaceException rather than
        // being retried. A batch handler is handed one reader and cannot issue another statement, so
        // there is no redo available here -- the choice is a wrong aggregate or an exception, and for a
        // fetch whose whole purpose is to decide what to write, the exception is the better answer.
        return new ForWritingQueryHandler(this, id, forUpdate, null, true);
    }

    /// <summary>
    ///     A <c>FetchForWriting</c> inside a batch: the batched <c>FetchForWriting</c> and
    ///     <c>FetchForExclusiveWriting</c>, either of them with an expected version, and every stream of
    ///     <c>FetchManyForWriting</c>. The single <c>FetchForWriting(id, expectedVersion)</c> runs it on its own.
    /// </summary>
    /// <remarks>
    ///     #6086: with the aggregate write cache, the baseline is taken when the batch is built, and the database
    ///     decides whether it can be folded onto. See <see cref="writeCachedReads" />.
    /// </remarks>
    public class ForWritingQueryHandler: IQueryHandler<IEventStream<TDoc>>, IExclusiveFetchHandler
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly long? _expectedStartingVersion;
        private readonly bool _detectSnapshotRace;
        private readonly LoadByIdHandler<TDoc, TId> _loadHandler;
        private AggregateWriteBaseline<TDoc> _baseline;

        public object? ExclusiveStreamId => _forUpdate ? _id : null;

        public ForWritingQueryHandler(FetchAsyncPlan<TDoc, TId> parent, TId id, bool forUpdate,
            long? expectedStartingVersion, bool detectSnapshotRace)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;
            _expectedStartingVersion = expectedStartingVersion;
            _detectSnapshotRace = detectSnapshotRace;
            _loadHandler = new LoadByIdHandler<TDoc, TId>(parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            var documentSession = (DocumentSessionBase)session;
            if (_parent._cache != null)
            {
                _baseline = documentSession.AggregateWriteCache.TakeBaseline<TDoc>(_parent._cache,
                    _parent.cacheKeyFor(documentSession, _id));
            }

            if (_baseline.HasAggregate)
            {
                _parent.writeCachedReads(builder, session, _id, _forUpdate, _loadHandler, _baseline.Version);
            }
            else
            {
                _parent.writeReads(builder, session, _id, _forUpdate, _loadHandler);
            }
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSession = (DocumentSessionBase)session;
            return _baseline.HasAggregate
                ? _parent.readCachedIntoStream(documentSession, _id, token, reader, _loadHandler,
                    documentSession.EventStorage(), _baseline, _detectSnapshotRace, _expectedStartingVersion)
                : _parent.ReadIntoStream(documentSession, _id, token, reader, _loadHandler,
                    documentSession.EventStorage(), _baseline, _detectSnapshotRace, _expectedStartingVersion);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
