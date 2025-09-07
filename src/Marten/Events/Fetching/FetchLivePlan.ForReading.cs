using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    public async ValueTask<TDoc?> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        // Optimization for having called FetchForWriting, then FetchLatest on same session in short order
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            if (session.TryGetAggregateFromIdentityMap<IEventStream<TDoc>, TId>(id, out var stream))
            {
                var starting = stream.Aggregate;
                var appendedEvents = stream.Events;

                return await _aggregator.BuildAsync(appendedEvents, session, starting, id, _documentStorage, cancellation).ConfigureAwait(false);
            }
        }

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        var builder = new BatchBuilder{TenantId = session.TenantId};

        var handler = _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector);
        handler.ConfigureCommand(builder, session);

        var batch = builder.Compile();
        await using var reader =
            await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        return await _aggregator.BuildAsync(events, session, default, id, _documentStorage, cancellation).ConfigureAwait(false);
    }

    public IQueryHandler<TDoc?> BuildQueryHandler(QuerySession session, TId id)
    {
        return new ReadOnlyQueryHandler(this, id);
    }

    public class ReadOnlyQueryHandler: IQueryHandler<TDoc?>
    {
        private readonly FetchLivePlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;

        public ReadOnlyQueryHandler(FetchLivePlan<TDoc, TId> parent, TId id)
        {
            _parent = parent;
            _id = id;

            _handler = parent._identityStrategy.BuildEventQueryHandler(parent.IsGlobal, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _handler.ConfigureCommand(builder, session);
        }

        public TDoc? Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public async Task<TDoc?> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var events = await _handler.HandleAsync(reader, session, token).ConfigureAwait(false);
            return await _parent._aggregator.BuildAsync(events, (QuerySession)session, default, _id, _parent._documentStorage, token).ConfigureAwait(false);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }
}
