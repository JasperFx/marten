using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Fetching;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        if (_cache == null)
        {
            return await fetchForWriting(session, id, forUpdate, default, cancellation).ConfigureAwait(false);
        }

        // #5225: claimed through the session's AggregateWriteCacheSession (jasperfx#1050). The claim removes the
        // entry, so only this fetch can fold onto the instance, and it is owed back to the cache only after a
        // commit this session owns.
        var baseline = session.AggregateWriteCache.TakeBaseline<TDoc>(_cache, cacheKeyFor(session, id));

        try
        {
            return await fetchForWriting(session, id, forUpdate, baseline, cancellation).ConfigureAwait(false);
        }
        catch (UnusableCachedAggregateException)
        {
            // The cached aggregate can't be the start of this stream: it is ahead of the stream (a restore, a
            // rollback, a deleted stream), or the stream is archived. The claim already removed the entry. Redo the
            // fetch uncached, which keeps the slot, so the result is still written back after the commit.
            return await fetchForWriting(session, id, forUpdate, baseline.WithoutAggregate(), cancellation)
                .ConfigureAwait(false);
        }
    }

    private async Task<IEventStream<TDoc>> fetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        AggregateWriteBaseline<TDoc> baseline, CancellationToken cancellation)
    {
        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};

        IQueryHandler<IReadOnlyList<IEvent>> handler;
        if (baseline.HasAggregate)
        {
            // #5225: the same two statements, with the events limited to the ones after the cached aggregate, and
            // the stream's archived flag read with its version
            writeVersionAndArchivedStatement(builder, id, forUpdate);
            builder.StartNewCommand();
            handler = _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector,
                new WhereFragment("d.version > ?", baseline.Version));
        }
        else
        {
            _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);
            builder.StartNewCommand();
            handler = _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector);
        }

        handler.ConfigureCommand(builder, session);

        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, handler, baseline).ConfigureAwait(false);
        }
        catch (UnusableCachedAggregateException)
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
    ///     #5225: the stream version, as <see cref="IEventIdentityStrategy{TId}.BuildCommandForReadingVersionForStream" />
    ///     reads it, and whether the stream is archived. A cached aggregate of an archived stream is not what a Live
    ///     fetch answers: its events are left out of the read, so the uncached fetch finds none.
    /// </summary>
    private void writeVersionAndArchivedStatement(ICommandBuilder builder, TId id, bool forUpdate)
    {
        builder.Append("select version, is_archived from ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_streams where id = ");
        builder.AppendParameter(id);

        if (_events.TenancyStyle == TenancyStyle.Conjoined && !IsGlobal)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update");
        }
    }

    /// <summary>
    ///     Signals that a cached aggregate can't be used and the fetch must be redone uncached. Never escapes
    ///     <see cref="FetchForWriting(DocumentSessionBase, TId, bool, CancellationToken)" />.
    /// </summary>
    /// <remarks>
    ///     A <see cref="MartenException" /> for the same reason as FetchAsyncPlan's: every exception in the assembly
    ///     derives from it.
    /// </remarks>
    private sealed class UnusableCachedAggregateException: MartenException;

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, CancellationToken cancellation,
        DbDataReader reader, IQueryHandler<IReadOnlyList<IEvent>> handler, AggregateWriteBaseline<TDoc> baseline = default)
    {
        long version = 0;
        try
        {
            var archived = false;
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
                if (baseline.HasAggregate)
                {
                    archived = await reader.GetFieldValueAsync<bool>(1, cancellation).ConfigureAwait(false);
                }
            }

            if (baseline.HasAggregate && (archived || baseline.Version > version))
            {
                await drainAsync(reader, cancellation).ConfigureAwait(false);
                throw new UnusableCachedAggregateException();
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            _telemetry.RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.LivePlan);

            // The stream says there are events past the cached aggregate and the read returned none: they were
            // archived out from under it, and folding nothing would hand back a stale aggregate
            if (baseline.NeedsArchivalRefetch(events, version))
            {
                await drainAsync(reader, cancellation).ConfigureAwait(false);
                throw new UnusableCachedAggregateException();
            }

            TDoc? document;
            if (baseline.HasAggregate)
            {
                document = events.Count > 0
                    ? await _aggregator.BuildAsync(events, session, baseline.Aggregate, id, _documentStorage, cancellation).ConfigureAwait(false)
                    : baseline.Aggregate;
            }
            else
            {
                document = await _aggregator.BuildAsync(events, session, default, id, _documentStorage, cancellation).ConfigureAwait(false);
            }

            if (document != null)
            {
                _documentStorage.SetIdentity(document, id);
            }

            // The events statement runs after the version statement, so it can include an append committed in
            // between, and the aggregate is then at that event's version. That is the version this fetch answers
            // with, as the Async plan does since #5799: claiming the one read first would label an aggregate with
            // fewer events than it folded, and its commit would fail against an append it already accounts for.
            if (events.Count > 0 && events[^1].Version > version) version = events[^1].Version;

            // #5225: owed to the cache, at the version the instance reflects. Under the Live lifecycle nothing
            // applies the caller's appends to this instance, so the label stays true through the commit, and the
            // next fetch reads this session's events as the delta. A null registers nothing, as does a fetch with
            // no cache (default baseline).
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
        catch (UnusableCachedAggregateException)
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

    // Leaves the connection clean for the redo
    private static async Task drainAsync(DbDataReader reader, CancellationToken cancellation)
    {
        while (await reader.NextResultAsync(cancellation).ConfigureAwait(false))
        {
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        return new ForUpdateQueryHandler(this, id, forUpdate);
    }

    public class ForUpdateQueryHandler : IQueryHandler<IEventStream<TDoc>>, IExclusiveFetchHandler
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

        public object? ExclusiveStreamId => _forUpdate ? _id : null;

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
