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
using Marten.Services;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
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
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();
        configureColdBoundary(builder, id);

        var handler = _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector);
        handler.ConfigureCommand(builder, session);

        try
        {
            LiveRead read;
            await using (var reader =
                         await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
            {
                read = await readAsync(reader, session, handler, true, cancellation).ConfigureAwait(false);
            }

            return await buildStreamAsync(session, id, read, cancellation).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var locked = asStreamLocked(e, id);
            if (locked != null) throw locked;

            throw;
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        return new ForUpdateQueryHandler(this, id, forUpdate);
    }

    public class ForUpdateQueryHandler : IFinishesAfterTheReader<IEventStream<TDoc>>
    {
        private readonly FetchLivePlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;
        private LiveRead _read;

        public ForUpdateQueryHandler(FetchLivePlan<TDoc, TId> parent, TId id, bool forUpdate)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;

            _handler = _parent._identityStrategy.BuildEventQueryHandler(_parent.IsGlobal, _id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();
            _parent.configureColdBoundary(builder, _id);

            _handler.ConfigureCommand(builder, session);
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            try
            {
                _read = await _parent.readAsync(reader, session, _handler, true, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var locked = asStreamLocked(e, _id);
                if (locked != null) throw locked;

                throw;
            }
        }

        public Task<IEventStream<TDoc>> FinishAsync(IStorageSession session, CancellationToken token)
        {
            return _parent.buildStreamAsync((DocumentSessionBase)session, _id, _read, token);
        }

        public async Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            await ReadAsync(reader, session, token).ConfigureAwait(false);
            return await FinishAsync(session, token).ConfigureAwait(false);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
