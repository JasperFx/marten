using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

using Marten.Internal;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>
{
    public async ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        var storage = findDocumentStorage(session);

        // Opting into optimizations here
        if (session.TryGetAggregateFromIdentityMap<TDoc, TId>(id, out var doc))
        {
            return doc;
        }

        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var builder = new BatchBuilder { TenantId = session.TenantId };
        builder.Append(";");

        var handler = new LoadByIdHandler<TDoc, TId>((IDocumentStorage<TDoc, TId>)storage, id);
        handler.ConfigureCommand(builder, session);

        TDoc? document;
        await using (var reader =
                     await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        }

        return document ?? await startFromColdAsync(session, storage, id, cancellation).ConfigureAwait(false);
    }

    public async ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        var snapshot = await FetchForReading(session, id, cancellation).ConfigureAwait(false);

        var pendingEvents = FetchPlanHelper.FindPendingEvents<TId>(session, id);
        if (pendingEvents is not { Count: > 0 }) return snapshot;

        var storage = findDocumentStorage(session);
        var aggregator = aggregatorFor(storage);

        snapshot = await aggregator.BuildAsync(pendingEvents, session, snapshot, id, storage, cancellation)
            .ConfigureAwait(false);

        // Evict the aggregate from the identity map: for mutable aggregate types
        // BuildAsync mutates `snapshot` in place, so the cached entry now reflects
        // post-pending-events state. Leaving it in the cache would cause the inline
        // projection during SaveChangesAsync to re-apply the same pending events
        // on top of the already-mutated aggregate. The configured inline projection
        // persists the document during SaveChangesAsync, so we don't store it here.
        session.EjectAggregateFromIdentityMap<TDoc, TId>(id);

        return snapshot;
    }

    public async Task<bool> StreamForReading(DocumentSessionBase session, TId id, Stream destination, CancellationToken cancellation)
    {
        var storage = findDocumentStorage(session);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);
        var command = ((IDocumentStorage<TDoc, TId>)storage).BuildLoadCommand(id, session.TenantId);
        if (await session.StreamOne(command, destination, cancellation).ConfigureAwait(false)) return true;

        var start = await startFromColdAsync(session, storage, id, cancellation).ConfigureAwait(false);
        if (start == null) return false;

        using var buffer = new Services.PooledByteBufferWriter();
        session.Serializer.WriteTo(buffer, start);
        await destination.WriteAsync(buffer.WrittenMemory, cancellation).ConfigureAwait(false);
        return true;
    }

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
    {
        var storage = findDocumentStorage(session);

        // Opting into optimizations here
        if (session is DocumentSessionBase dsb && dsb.TryGetAggregateFromIdentityMap<TDoc, TId>(id, out var doc))
        {
            return new PreCannedQueryHandler<TDoc?>(doc);
        }

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        return coldStartFor(storage) == null ? handler : new LatestQueryHandler(this, id, storage, handler);
    }

    /// <summary>A batched FetchLatest that may start from the cold part once the batch's reader is closed.</summary>
    internal class LatestQueryHandler: IFinishesAfterTheReader<TDoc?>
    {
        private readonly FetchInlinedPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly IDocumentStorage<TDoc, TId> _storage;
        private readonly LoadByIdHandler<TDoc, TId> _handler;
        private TDoc? _document;

        public LatestQueryHandler(FetchInlinedPlan<TDoc, TId> parent, TId id, IDocumentStorage<TDoc, TId> storage,
            LoadByIdHandler<TDoc, TId> handler)
        {
            _parent = parent;
            _id = id;
            _storage = storage;
            _handler = handler;
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _handler.ConfigureCommand(builder, session);
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            _document = await _handler.HandleAsync(reader, session, token).ConfigureAwait(false);
        }

        public async Task<TDoc?> FinishAsync(IStorageSession session, CancellationToken token)
        {
            return _document ?? await _parent.startFromColdAsync((QuerySession)session, _storage, _id, token)
                .ConfigureAwait(false);
        }

        public async Task<TDoc?> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            await ReadAsync(reader, session, token).ConfigureAwait(false);
            return await FinishAsync(session, token).ConfigureAwait(false);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            return _handler.StreamJson(stream, reader, token);
        }
    }
}
