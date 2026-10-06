using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;
using Marten.Util;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{
    public async ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        // Optimization for having called FetchForWriting, then FetchLatest on same session in short order
        if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
        {
            if (session.TryGetAggregateFromIdentityMap<IEventStream<TDoc>, TId>(id, out var stream))
            {
                var starting = stream.Aggregate;
                var appendedEvents = stream.Events;

                return await _aggregator.BuildAsync(appendedEvents, session, starting, id, _storage, cancellation).ConfigureAwait(false);
            }
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        var builder = new BatchBuilder{TenantId = session.TenantId};

        // #5604. The two reads below have exactly the race FetchForWriting was fixed for: the snapshot
        // document and then the events after it, each taking its own snapshot under READ COMMITTED, with
        // the delta query re-reading a.mt_version. A daemon snapshot write landing in between makes the
        // events read exclude the very events the document read never saw, so FetchLatest answers null
        // for a live stream or silently stale state.
        var sharedSnapshot = beginSharedSnapshot(builder, session);

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        endSharedSnapshot(builder, sharedSnapshot);

        var batch = builder.Compile();
        await using var reader =
            await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        return await readLatest(session, id, cancellation, loadHandler, reader, selector).ConfigureAwait(false);
    }

    public async ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        var snapshot = await FetchForReading(session, id, cancellation).ConfigureAwait(false);

        var pendingEvents = FetchPlanHelper.FindPendingEvents<TId>(session, id);
        if (pendingEvents is not { Count: > 0 }) return snapshot;

        snapshot = await _aggregator.BuildAsync(pendingEvents, session, snapshot, id, _storage, cancellation)
            .ConfigureAwait(false);

        // Store the updated document so it persists when the session commits
        if (snapshot != null)
        {
            session.Store(snapshot);
        }

        return snapshot;
    }

    public async Task<bool> StreamForReading(DocumentSessionBase session, TId id, Stream destination, CancellationToken cancellation)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        var builder = new BatchBuilder { TenantId = session.TenantId };

        // #5604, as in FetchForReading above -- the streaming variant reads the same two things in the
        // same order and had the same race.
        var sharedSnapshot = beginSharedSnapshot(builder, session);

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        endSharedSnapshot(builder, sharedSnapshot);

        var batch = builder.Compile();
        await using var reader = await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        // First result set: buffer raw JSONB (don't deserialize yet)
        bool hasDocument = await reader.ReadAsync(cancellation).ConfigureAwait(false);
        MemoryStream? rawJsonBuffer = null;

        if (hasDocument)
        {
            var ordinal = reader.GetOrdinal("data");
            if (!await reader.IsDBNullAsync(ordinal, cancellation).ConfigureAwait(false))
            {
                rawJsonBuffer = SharedMemoryStreamManager.GetStream();
                var source = await ((NpgsqlDataReader)reader).GetStreamAsync(ordinal, cancellation).ConfigureAwait(false);
                await source.CopyStreamSkippingSOHAsync(rawJsonBuffer, cancellation).ConfigureAwait(false);
            }
        }

        // Second result set: check for newer events
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var events = await new ListQueryHandler<IEvent>(null, selector)
            .HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        if (!events.Any() && rawJsonBuffer != null)
        {
            // Caught up — stream raw JSONB directly (zero deserialization)
            rawJsonBuffer.Position = 0;
            await rawJsonBuffer.CopyToAsync(destination, cancellation).ConfigureAwait(false);
            return true;
        }

        if (rawJsonBuffer == null && !events.Any())
            return false;

        // Not caught up — deserialize stored doc, rebuild with new events, serialize
        TDoc? document = null;
        if (rawJsonBuffer != null)
        {
            rawJsonBuffer.Position = 0;
            document = session.Serializer.FromJson<TDoc>(rawJsonBuffer);
        }

        document = await _aggregator.BuildAsync(events, session, document, id, _storage, cancellation).ConfigureAwait(false);
        if (document == null) return false;

        _storage.SetIdentity(document, id);
        // Direct UTF-8 serialization into a pooled buffer, then a single async write —
        // avoids ToJson → string → UTF-8 GetBytes → write round-trip.
        using var buffer = new Services.PooledByteBufferWriter();
        session.Serializer.WriteTo(buffer, document);
        await destination.WriteAsync(buffer.WrittenMemory, cancellation).ConfigureAwait(false);
        return true;
    }

    private async Task<TDoc?> readLatest(DocumentSessionBase session, TId id, CancellationToken cancellation,
        LoadByIdHandler<TDoc, TId> loadHandler, DbDataReader reader, IEventStorage selector)
    {
        // Fetch the existing aggregate -- if any!
        var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        // Read in any events from after the current state of the aggregate
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        if (events.Any())
        {
            document = await _aggregator.BuildAsync(events, session, document, id, _storage, cancellation).ConfigureAwait(false);
        }

        if (document != null)
        {
            _storage.SetIdentity(document, id);
        }

        return document;
    }


    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
    {
        if (_initialSql.IsEmpty())
        {
            ensureInitialSql(session.EventStorage());
        }

        return new QueryHandler(this, id, canShareOneSnapshot(session));
    }

    public class QueryHandler: IQueryHandler<TDoc?>, IOpensItsOwnTransaction
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _sharedSnapshot;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        /// <summary>
        ///     #5604: a batched <c>FetchLatest</c> brackets its two reads for the same reason the direct
        ///     call does, so it takes on the same two batch rules — it has to be the batch's first
        ///     operation, and it cannot share a batch with an exclusive fetch whose row lock its
        ///     <c>end</c> would release. False inside a caller-owned transaction, where #5611 suppresses
        ///     the bracket and so leaves nothing for either rule to protect.
        /// </summary>
        public bool OpensItsOwnTransaction => _sharedSnapshot;

        public string CallName => "FetchLatest";

        public QueryHandler(FetchAsyncPlan<TDoc, TId> parent, TId id, bool sharedSnapshot)
        {
            _parent = parent;
            _id = id;
            _sharedSnapshot = sharedSnapshot;

            _loadHandler = new LoadByIdHandler<TDoc, TId>(parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            // Asked again here rather than reusing _sharedSnapshot -- see ForUpdateQueryHandler for why.
            var sharedSnapshot = beginSharedSnapshot(builder, (QuerySession)session);

            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);

            endSharedSnapshot(builder, sharedSnapshot);
        }

        public Task<TDoc?> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            var eventStorage = documentSessionBase.EventStorage();
            return _parent.readLatest(documentSessionBase, _id, token, _loadHandler, reader, eventStorage);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

    }
}
