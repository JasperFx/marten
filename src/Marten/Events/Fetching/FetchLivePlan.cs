using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
{
    private readonly ILiveAggregator<TDoc> _aggregator;
    private readonly IDocumentStorage<TDoc, TId> _documentStorage;
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> documentStorage)
    {
        _events = events;
        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;
        _aggregator = _events.Options.Projections.AggregatorFor<TDoc>();
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, forUpdate);

        builder.StartNewCommand();

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        long version = 0;
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            var document = await _aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);
            if (document != null)
            {
                _documentStorage.SetIdentity(document, id);
            }

            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion,
        CancellationToken cancellation = default)
    {

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);

        builder.StartNewCommand();

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        long version = 0;
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            if (expectedStartingVersion != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {expectedStartingVersion}, but was {version}",
                    typeof(TDoc), id);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            var document = await _aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);


            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }
}
