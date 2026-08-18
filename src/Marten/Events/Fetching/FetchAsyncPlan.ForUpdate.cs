using System;
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
using Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{

    [MemberNotNull(nameof(_initialSql))]
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate, CancellationToken cancellation = default)
    {
        try
        {
            return await fetchForWriting(session, id, forUpdate, true, cancellation).ConfigureAwait(false);
        }
        catch (CacheAheadOfDatabaseException)
        {
            // The cached snapshot claimed a higher version than the stream actually has, so the delta
            // query could not possibly reconstitute it. TryTake already removed the entry; redo the fetch
            // on the normal, always-correct uncached path -- but still write the result back, so the cache
            // heals in one round instead of two. Rare enough to not be worth optimizing further.
            return await fetchForWriting(session, id, forUpdate, false, cancellation).ConfigureAwait(false);
        }
    }

    [MemberNotNull(nameof(_initialSql))]
    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        bool useCachedSnapshot, CancellationToken cancellation)
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
        if (!forUpdate)
        {
            builder.Append("begin transaction isolation level repeatable read read only");
            builder.StartNewCommand();
        }

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

        if (!forUpdate)
        {
            builder.StartNewCommand();
            builder.Append("end");
        }

        var batch = builder.Compile();
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, loadHandler, selector,
                new CacheAttempt(cache, cacheKey, cacheHit, cachedAggregate, cachedVersion)).ConfigureAwait(false);
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
        CacheAttempt cacheAttempt = default)
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
            if (!_forUpdate)
            {
                builder.Append("begin transaction isolation level repeatable read read only");
                builder.StartNewCommand();
            }

            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();

            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);

            if (!_forUpdate)
            {
                builder.StartNewCommand();
                builder.Append("end");
            }
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            return _parent.ReadIntoStream(documentSessionBase, _id, token, reader, _loadHandler, documentSessionBase.EventStorage());
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
