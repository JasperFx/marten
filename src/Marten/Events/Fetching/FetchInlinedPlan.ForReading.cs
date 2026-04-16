using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

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

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
        var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        return document;
    }

    public async ValueTask<TDoc?> ProjectLatest(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        var snapshot = await FetchForReading(session, id, cancellation).ConfigureAwait(false);

        var pendingEvents = FetchPlanHelper.FindPendingEvents<TId>(session, id);
        if (pendingEvents is not { Count: > 0 }) return snapshot;

        // Build the aggregator on demand
        var raw = session.Options.Projections.AggregatorFor<TDoc>();
        var storage = findDocumentStorage(session);
        var aggregator = raw as IAggregator<TDoc, TId, IQuerySession>
                         ?? typeof(IdentityForwardingAggregator<,,,>)
                             .CloseAndBuildAs<IAggregator<TDoc, TId, IQuerySession>>(raw, storage, typeof(TDoc),
                                 storage is IDocumentStorage<TDoc, TId> s ? s.IdType : typeof(TId),
                                 typeof(TId), typeof(IQuerySession));

        snapshot = await aggregator.BuildAsync(pendingEvents, session, snapshot, id, storage, cancellation)
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
        var storage = findDocumentStorage(session);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);
        var command = ((IDocumentStorage<TDoc, TId>)storage).BuildLoadCommand(id, session.TenantId);
        return await session.StreamOne(command, destination, cancellation).ConfigureAwait(false);
    }

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
    {
        var storage = findDocumentStorage(session);

        // Opting into optimizations here
        if (session is DocumentSessionBase dsb && dsb.TryGetAggregateFromIdentityMap<TDoc, TId>(id, out var doc))
        {
            return new PreCannedQueryHandler<TDoc?>(doc);
        }

        return new LoadByIdHandler<TDoc, TId>(storage, id);
    }
}
