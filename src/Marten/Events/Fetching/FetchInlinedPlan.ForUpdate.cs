using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        IDocumentStorage<TDoc, TId>? storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(session.TrackingMode);
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, forUpdate);

        builder.StartNewCommand();

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
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
        DbDataReader reader, LoadByIdHandler<TDoc, TId> handler)
    {
        long version = 0;
        try
        {
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // As an optimization, put the document in the identity map for later
            if (document != null && session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, document);
            }

            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
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
        var storage = findDocumentStorage(session);

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        return new QueryHandler(this, id, handler, forUpdate);
    }

    internal class QueryHandler: IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchInlinedPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly LoadByIdHandler<TDoc, TId> _handler;
        private readonly bool _forUpdate;

        public QueryHandler(FetchInlinedPlan<TDoc, TId> parent, TId id, LoadByIdHandler<TDoc, TId> handler,
            bool forUpdate)
        {
            _parent = parent;
            _id = id;
            _handler = handler;
            _forUpdate = forUpdate;
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, _forUpdate);

            builder.StartNewCommand();

            _handler.ConfigureCommand(builder, session);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            return _parent.ReadIntoStream((DocumentSessionBase)session, _id, token, reader, _handler);
        }

        #region stuff we don't care about

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotImplementedException();
        }


        public IEventStream<TDoc> Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
