using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
