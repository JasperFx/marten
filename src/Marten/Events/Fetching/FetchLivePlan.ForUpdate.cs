using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Fetching;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        try
        {
            return await fetchForWriting(session, id, forUpdate, true, cancellation).ConfigureAwait(false);
        }
        catch (CachedSnapshotUnusableException)
        {
            // Either the entry claimed a higher version than the stream has, or the stream has since been
            // archived. TryTake already removed it; redo the fetch on the uncached path, which replays the
            // whole stream and is always correct.
            return await fetchForWriting(session, id, forUpdate, false, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        bool useCachedSnapshot, CancellationToken cancellation)
    {
        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var cache = _cache;
        var cacheKey = cache == null ? default : cacheKeyFor(session, id);
        object? cachedAggregate = null;
        var cachedVersion = 0L;
        var cacheHit = cache != null && useCachedSnapshot &&
                       cache.TryTake(cacheKey, out cachedAggregate, out cachedVersion);

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();

        // The whole cost this cache removes under Live is reading and folding the events the cached
        // snapshot already accounts for, so a hit narrows the event query to the delta after it. This is
        // FetchAsyncPlan's strategy with the baseline coming from memory instead of the document table.
        var handler = cacheHit
            ? _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector,
                new WhereFragment("d.version > ?", cachedVersion))
            : _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector);
        handler.ConfigureCommand(builder, session);

        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, handler,
                new CacheAttempt(cache, cacheKey, cacheHit, cachedAggregate, cachedVersion)).ConfigureAwait(false);
        }
        catch (CachedSnapshotUnusableException)
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
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
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
    ///     Signals that a cached snapshot cannot serve as a baseline and the fetch must be redone uncached.
    ///     Never escapes <see cref="FetchForWriting" />.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="MartenException" /> for the same reason as the equivalents in the Async
    ///     and Inline plans: the assembly-wide convention test does not exempt nested private types, and
    ///     should not.
    /// </remarks>
    private sealed class CachedSnapshotUnusableException: MartenException;

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, CancellationToken cancellation,
        DbDataReader reader, IQueryHandler<IReadOnlyList<IEvent>> handler, CacheAttempt cacheAttempt = default)
    {
        long version = 0;
        var archived = false;
        try
        {
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
                archived = await reader.GetFieldValueAsync<bool>(1, cancellation).ConfigureAwait(false);
            }

            if (cacheAttempt.Hit && (cacheAttempt.Version > version || archived))
            {
                // Either the cache is ahead of the database -- a restore, a rollback, or a key collision
                // bug -- so the delta could not reconstitute it; or the stream has been archived, and an
                // uncached fetch would answer null rather than the aggregate this entry still holds.
                // Nothing here can recover, so drain the batch to leave the connection clean and let
                // FetchForWriting retry uncached.
                while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
                {
                }

                throw new CachedSnapshotUnusableException();
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            _telemetry.RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.LivePlan);

            // On a hit the events read are only the delta after the cached snapshot, so that snapshot is
            // the baseline the fold starts from. An empty delta returns the baseline unchanged.
            var document = cacheAttempt.Hit
                ? await _aggregator.BuildAsync(events, session, (TDoc)cacheAttempt.Aggregate!, id, _documentStorage, cancellation).ConfigureAwait(false)
                : await _aggregator.BuildAsync(events, session, default, id, _documentStorage, cancellation).ConfigureAwait(false);
            if (document != null)
            {
                _documentStorage.SetIdentity(document, id);
            }

            // The aggregate is now at the stream version that came out of the database, which is what the
            // next fetch measures its delta against. Stored here rather than after the commit because
            // take-on-read already made this caller the only owner of the instance, and under Live nothing
            // in the commit applies the caller's new events to it.
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
        catch (CachedSnapshotUnusableException)
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
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        return new ForUpdateQueryHandler(this, id, forUpdate);
    }

    public class ForUpdateQueryHandler : IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchLivePlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;

        public ForUpdateQueryHandler(FetchLivePlan<TDoc, TId> parent, TId id, bool forUpdate)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;

            _handler = _parent._identityStrategy.BuildEventQueryHandler(_parent.IsGlobal, _id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();

            _handler.ConfigureCommand(builder, session);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            return _parent.ReadIntoStream((DocumentSessionBase)session, _id, token, reader, _handler);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
