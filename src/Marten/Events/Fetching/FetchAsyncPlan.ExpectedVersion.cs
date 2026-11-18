using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{
    /// <remarks>
    ///     #6086: the same reads as the batched fetch, so the aggregate write cache is used here too, and redone
    ///     like the plain fetch when the snapshot went backwards between the reads. The expected version is
    ///     checked against the version the database says the aggregate reached, whatever the cache holds.
    /// </remarks>
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        ensureInitialSql(selector);

        return await withSnapshotRaceRedos(session, id, async detect =>
        {
            var handler = new ForWritingQueryHandler(this, id, false, expectedStartingVersion, detect);
            var builder = new BatchBuilder{TenantId = session.TenantId};
            handler.ConfigureCommand(builder, session);

            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, long expectedStartingVersion)
    {
        var dsb = session.AssertIsDocumentSession();
        if (_initialSql.IsEmpty())
        {
            ensureInitialSql(dsb.EventStorage());
        }

        // Detection ON: a batch cannot redo itself, so a snapshot that went backwards is refused with
        // SnapshotRaceException rather than answered wrongly. See BuildQueryHandler(session, id, forUpdate).
        return new ForWritingQueryHandler(this, id, false, expectedStartingVersion, true);
    }
}
