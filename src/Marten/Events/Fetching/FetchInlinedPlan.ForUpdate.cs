using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Storage;
using Marten.Services.BatchQuerying;
using Npgsql;
using Weasel.Postgresql;
using JasperFx.Events.Fetching;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>
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
            // The cached snapshot was not at the stream's current version. Unlike the Async plan there is
            // no delta query to reconcile with -- an Inline snapshot is always exactly at the stream head,
            // so anything else means the entry is simply wrong. TryTake has already removed it; redo the
            // fetch on the always-correct uncached path.
            return await fetchForWriting(session, id, forUpdate, false, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        bool useCachedSnapshot, CancellationToken cancellation)
    {
        IDocumentStorage<TDoc, TId>? storage = null;
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            storage = ((IMartenSession)session).Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = ((IMartenSession)session).Options.ResolveCorrectedDocumentStorage<TDoc, TId>(session.TrackingMode);
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        // jasperfx#1050: claimed through the session's AggregateWriteCacheSession. See FetchAsyncPlan.
        var baseline = _cache == null
            ? default
            : session.AggregateWriteCache.TakeBaseline<TDoc>(_cache, cacheKeyFor(session, id));
        if (!useCachedSnapshot) baseline = baseline.WithoutAggregate();

        var cacheHit = baseline.HasAggregate;

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);

        // Under Inline the stored snapshot is written in the same transaction as the events, so it is
        // always exactly at the stream head -- there is no delta to fold. That makes the snapshot load the
        // *whole* cost this cache exists to remove: the doc row, and the JSON deserialize of it.
        if (!cacheHit)
        {
            builder.StartNewCommand();
            handler.ConfigureCommand(builder, session);
        }

        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, handler, storage, baseline)
                .ConfigureAwait(false);
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
    ///     Signals that a cached snapshot did not match the stream's current version and the fetch must be
    ///     redone uncached. Never escapes <see cref="FetchForWriting" />.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="MartenException" /> for the same reason as
    ///     <c>FetchAsyncPlan.CacheAheadOfDatabaseException</c>: the assembly-wide convention test does not
    ///     exempt nested private types, and should not.
    /// </remarks>
    private sealed class CachedSnapshotUnusableException: MartenException;

    /// <param name="conditionalLoad">
    ///     #6086: a batched fetch with a cached aggregate. It can't redo itself, so its snapshot statement is in the
    ///     batch, and only returns the snapshot when the stream is not at the cached version (see
    ///     <see cref="QueryHandler" />).
    /// </param>
    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, CancellationToken cancellation,
        DbDataReader reader, LoadByIdHandler<TDoc, TId> handler, IDocumentStorage<TDoc, TId>? storage = null,
        AggregateWriteBaseline<TDoc> cacheAttempt = default, long? expectedStartingVersion = null,
        bool conditionalLoad = false)
    {
        long version = 0;
        try
        {
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            if (expectedStartingVersion.HasValue && expectedStartingVersion.Value != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {expectedStartingVersion.Value}, but was {version}",
                    typeof(TDoc), id);
            }

            TDoc? document;
            if (cacheAttempt.HasAggregate && cacheAttempt.Version != version && conditionalLoad)
            {
                // The snapshot statement found the stream off the cached version too, and loaded the snapshot.
                // The cached aggregate was claimed, so it is gone from the cache either way; the result still
                // goes back after the commit.
                cacheAttempt = cacheAttempt.WithoutAggregate();
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            }
            else if (cacheAttempt.HasAggregate)
            {
                if (cacheAttempt.Version != version)
                {
                    // An Inline snapshot is durably at the stream head, so a version that is not an exact
                    // match means the entry is stale or wrong -- and there is no delta query in this plan to
                    // reconcile it with. The snapshot was never requested in this batch, so drain the reader
                    // to leave the connection clean and let FetchForWriting retry uncached.
                    while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
                    {
                    }

                    throw new CachedSnapshotUnusableException();
                }

                document = cacheAttempt.Aggregate;

                // The batched fetch's snapshot statement, which is empty here, and is this fetch's last result
                if (conditionalLoad)
                {
                    await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                }
            }
            else
            {
                await reader.NextResultAsync(cancellation).ConfigureAwait(false);
                document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            }

            // #5227: always zero -- an Inline snapshot is up to date by construction, so nothing is
            // replayed on either path. A cache hit does not change that; what it removes is the
            // snapshot LOAD, which this histogram does not measure. Recorded anyway so the histogram
            // can be compared across lifecycles.
            _events.Options.OpenTelemetry
                .RecordEventsReplayed(0, _aggregateTypeName, OpenTelemetryOptions.InlinePlan);

            // As an optimization, put the document in the identity map for later. On a cache hit this is
            // what lets the inline projection reuse the very instance we just handed out when it applies
            // the caller's events during commit, instead of loading the snapshot a second time.
            if (document != null && ((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, document);
            }

            // jasperfx#1050: owed to the cache at the version it was fetched at, which is what this
            // instance reflects. Under Inline the commit's own projection moves the aggregate on, so the
            // write-back is advanced to whatever instance that projection stored. Relabelling the fetched
            // instance with the committed version, as Marten used to, was false whenever Apply returns a
            // new instance or UseIdentityMapForAggregates is off.
            if (_cache != null && document != null && version > 0 && storage != null)
            {
                session.AggregateWriteCache.Register(cacheAttempt, document, version);
                AdvanceAfterCommit(session, storage, cacheKeyFor(session, id), id, IsGlobal);
            }

            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
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
    ///     After a commit this session owns: if it appended to this stream, write back the instance the
    ///     inline projection stored for it, at the stream's committed version. Exactly one candidate or
    ///     nothing is written back, because guessing would put a wrong aggregate where the next writer
    ///     decides from it.
    /// </summary>
    internal static void AdvanceAfterCommit(DocumentSessionBase session, IDocumentStorage<TDoc, TId> storage,
        AggregateCacheKey key, TId id, bool isGlobal)
    {
        var tenantId = session.TenantId;

        session.OnAggregateCommitted(commit =>
        {
            StreamAction? stream = null;
            foreach (var candidate in commit.GetStreams())
            {
                var sameStream = id is Guid guid ? candidate.Id == guid : Equals(candidate.Key, id as string);
                if (sameStream && (isGlobal || candidate.TenantId == tenantId))
                {
                    stream = candidate;
                    break;
                }
            }

            // Nothing appended: the registered instance is still exactly what was fetched
            if (stream == null || stream.Events.Count == 0) return;

            TDoc? projected = null;
            foreach (var document in commit.Updated.Concat(commit.Inserted).OfType<TDoc>())
            {
                if (!EqualityComparer<TId>.Default.Equals(storage.Identity(document), id)) continue;
                if (ReferenceEquals(projected, document)) continue;

                if (projected != null)
                {
                    session.AggregateWriteCache.Discard(key);
                    return;
                }

                projected = document;
            }

            // A projection that deleted the aggregate, or stored nothing for it, leaves nothing to cache
            if (projected == null || !session.AggregateWriteCache.Advance(key, projected, stream.Version))
            {
                session.AggregateWriteCache.Discard(key);
            }
        });
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        var storage = findDocumentStorage(session);

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        return new QueryHandler(this, id, handler, storage, forUpdate, null);
    }

    /// <summary>
    ///     A <c>FetchForWriting</c> inside a batch: the batched <c>FetchForWriting</c> and
    ///     <c>FetchForExclusiveWriting</c>, either of them with an expected version, and every stream of
    ///     <c>FetchManyForWriting</c>. The single <c>FetchForWriting(id, expectedVersion)</c> runs it on its own.
    /// </summary>
    /// <remarks>
    ///     #6086: with the aggregate write cache, the baseline is taken when the batch is built. A batch can't redo
    ///     one fetch, so the snapshot statement stays in it, with a condition that leaves the snapshot out when the
    ///     stream is at the cached version:
    ///     <code>
    ///     select version from mt_streams where id = :id [for update];
    ///     select [snapshot] where id = :id and not exists (select 1 from mt_streams as s where s.id = :id and s.version = :cached)
    ///     </code>
    ///     The version statement decides. In the one case the two can disagree, a stream deleted and written again
    ///     up to the cached version in between, the fetch finds no snapshot, as an uncached fetch racing that delete
    ///     could.
    /// </remarks>
    internal class QueryHandler: IQueryHandler<IEventStream<TDoc>>, IExclusiveFetchHandler
    {
        private readonly FetchInlinedPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly LoadByIdHandler<TDoc, TId> _handler;
        private readonly IDocumentStorage<TDoc, TId> _storage;
        private readonly bool _forUpdate;
        private readonly long? _expectedStartingVersion;
        private AggregateWriteBaseline<TDoc> _baseline;

        public QueryHandler(FetchInlinedPlan<TDoc, TId> parent, TId id, LoadByIdHandler<TDoc, TId> handler,
            IDocumentStorage<TDoc, TId> storage, bool forUpdate, long? expectedStartingVersion)
        {
            _parent = parent;
            _id = id;
            _handler = handler;
            _storage = storage;
            _forUpdate = forUpdate;
            _expectedStartingVersion = expectedStartingVersion;
        }

        public object? ExclusiveStreamId => _forUpdate ? _id : null;

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            var documentSession = (DocumentSessionBase)session;
            if (_parent._cache != null)
            {
                _baseline = documentSession.AggregateWriteCache.TakeBaseline<TDoc>(_parent._cache,
                    _parent.cacheKeyFor(documentSession, _id));
            }

            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();

            _handler.ConfigureCommand(builder, session);

            if (_baseline.HasAggregate)
            {
                var streamTenant = _parent._events.TenancyStyle == TenancyStyle.Conjoined && !_parent.IsGlobal
                    ? builder.TenantId
                    : null;

                builder.Append(" and not ");
                CachedAggregateSql.WriteStreamAtVersion(builder, _parent._events, _id, _baseline.Version, streamTenant);
            }
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            return _parent.ReadIntoStream((DocumentSessionBase)session, _id, token, reader, _handler, _storage,
                _baseline, _expectedStartingVersion, true);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}
