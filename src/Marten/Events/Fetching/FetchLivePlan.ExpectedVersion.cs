using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    /// <remarks>
    ///     #6086: the same reads as the batched fetch, so the aggregate write cache is used here too. The expected
    ///     version is checked against the stream's version as the database reports it, whatever the cache holds.
    /// </remarks>
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion,
        CancellationToken cancellation = default)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);

        var handler = new ForWritingQueryHandler(this, id, false, expectedStartingVersion);
        var builder = new BatchBuilder{TenantId = session.TenantId};
        handler.ConfigureCommand(builder, session);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

        return await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, long expectedStartingVersion)
    {
        session.AssertIsDocumentSession();
        return new ForWritingQueryHandler(this, id, false, expectedStartingVersion);
    }
}
