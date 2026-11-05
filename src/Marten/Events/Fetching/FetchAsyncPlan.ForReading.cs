using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
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

        ensureInitialSql(selector);

        return await withSnapshotRaceRedos(session, id, async detect =>
        {
            var builder = new BatchBuilder{TenantId = session.TenantId};
            var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
            writeLatestReads(builder, session, id, loadHandler);

            var batch = builder.Compile();
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            return await readLatest(session, id, cancellation, loadHandler, reader, selector, detect)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     The events, then the snapshot document, in that order. See <see cref="writeEventFetchStatement" />.
    /// </summary>
    private void writeLatestReads(ICommandBuilder builder, IStorageSession session, TId id,
        LoadByIdHandler<TDoc, TId> loadHandler)
    {
        writeEventFetchStatement(id, builder);

        builder.StartNewCommand();

        loadHandler.ConfigureCommand(builder, session);
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

        ensureInitialSql(selector);

        // A race is detected before anything is written to the destination, so a redo starts clean
        return await withSnapshotRaceRedos(session, id,
                detect => streamForReading(session, id, destination, selector, detect, cancellation))
            .ConfigureAwait(false);
    }

    private async Task<bool> streamForReading(DocumentSessionBase session, TId id, Stream destination,
        IEventStorage selector, bool detectSnapshotRace, CancellationToken cancellation)
    {
        var builder = new BatchBuilder { TenantId = session.TenantId };
        writeLatestReads(builder, session, id, new LoadByIdHandler<TDoc, TId>(_storage, id));

        var batch = builder.Compile();
        await using var reader = await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        // First result set: the newer events
        var delta = await readDeltaAsync(reader, selector, cancellation).ConfigureAwait(false);

        // Second result set: buffer raw JSONB (don't deserialize yet)
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        bool hasDocument = await reader.ReadAsync(cancellation).ConfigureAwait(false);
        MemoryStream? rawJsonBuffer = null;
        long? snapshotVersion = null;

        if (hasDocument)
        {
            snapshotVersion = await readSnapshotVersionAsync(reader, cancellation).ConfigureAwait(false);

            var ordinal = reader.GetOrdinal("data");
            if (!await reader.IsDBNullAsync(ordinal, cancellation).ConfigureAwait(false))
            {
                rawJsonBuffer = SharedMemoryStreamManager.GetStream();
                var source = await ((NpgsqlDataReader)reader).GetStreamAsync(ordinal, cancellation).ConfigureAwait(false);
                await source.CopyStreamSkippingSOHAsync(rawJsonBuffer, cancellation).ConfigureAwait(false);
            }
        }

        var events = eventsAfterSnapshot(id, rawJsonBuffer != null, snapshotVersion, delta, detectSnapshotRace);

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

    /// <summary>
    ///     The snapshot row's <c>mt_version</c>, for the streaming path, which reads the row raw rather than
    ///     through the selector that would record it in the session.
    /// </summary>
    private static async Task<long?> readSnapshotVersionAsync(DbDataReader reader, CancellationToken cancellation)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i) != "mt_version") continue;

            return await reader.IsDBNullAsync(i, cancellation).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<long>(i, cancellation).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<TDoc?> readLatest(DocumentSessionBase session, TId id, CancellationToken cancellation,
        LoadByIdHandler<TDoc, TId> loadHandler, DbDataReader reader, IEventStorage selector, bool detectSnapshotRace)
    {
        // The events after the snapshot, then the snapshot itself -- if any!
        var delta = await readDeltaAsync(reader, selector, cancellation).ConfigureAwait(false);

        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        var events = eventsAfterSnapshot(id, document != null, snapshotVersionFor(session, id), delta, detectSnapshotRace);
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

        return new QueryHandler(this, id);
    }

    public class QueryHandler: IQueryHandler<TDoc?>
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        public QueryHandler(FetchAsyncPlan<TDoc, TId> parent, TId id)
        {
            _parent = parent;
            _id = id;

            _loadHandler = new LoadByIdHandler<TDoc, TId>(parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent.writeLatestReads(builder, session, _id, _loadHandler);
        }

        public Task<TDoc?> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            var eventStorage = documentSessionBase.EventStorage();
            // Detection ON: a batch cannot redo itself. See ForUpdateQueryHandler.
            return _parent.readLatest(documentSessionBase, _id, token, _loadHandler, reader, eventStorage, true);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

    }
}
