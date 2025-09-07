using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
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

                return await _aggregator.BuildAsync(appendedEvents, session, starting, id, _storage, cancellation).ConfigureAwait(false);
            }
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        // TODO -- use read only transaction????

        var builder = new BatchBuilder{TenantId = session.TenantId};

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        var batch = builder.Compile();
        await using var reader =
            await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        return await readLatest(session, id, cancellation, loadHandler, reader, selector).ConfigureAwait(false);
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

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);
        }

        public Task<TDoc?> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            var eventStorage = documentSessionBase.EventStorage();
            return _parent.readLatest(documentSessionBase, _id, token, _loadHandler, reader, eventStorage);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public TDoc? Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }
    }
}
