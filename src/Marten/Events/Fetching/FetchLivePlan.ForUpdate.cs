using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Npgsql;
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
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, forUpdate);

        builder.StartNewCommand();

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, cancellation, reader, handler).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, CancellationToken cancellation,
        DbDataReader reader, IQueryHandler<IReadOnlyList<IEvent>> handler)
    {
        long version = 0;
        try
        {
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            var document = await _aggregator.BuildAsync(events, session, default, id, _documentStorage, cancellation).ConfigureAwait(false);
            if (document != null)
            {
                _documentStorage.SetIdentity(document, id);
            }

            var stream = version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);

            // This is an optimization for calling FetchForWriting, then immediately calling FetchLatest
            if (session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, stream);
            }

            return stream;
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, bool forUpdate)
    {
        session.AssertIsDocumentSession();
        return new ForUpdateQueryHandler(this, id, forUpdate);
    }

    public class ForUpdateQueryHandler : IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchLivePlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly bool _forUpdate;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;

        public ForUpdateQueryHandler(FetchLivePlan<TDoc, TId> parent, TId id, bool forUpdate)
        {
            _parent = parent;
            _id = id;
            _forUpdate = forUpdate;

            _handler = _parent._identityStrategy.BuildEventQueryHandler(_id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(builder, _id, _forUpdate);

            builder.StartNewCommand();

            _handler.ConfigureCommand(builder, session);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            return _parent.ReadIntoStream((DocumentSessionBase)session, _id, token, reader, _handler);
        }

        public IEventStream<TDoc> Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }
    }

}
