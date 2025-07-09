using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchInlinedPlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion, CancellationToken cancellation = default)
    {
        var storage = findDocumentStorage(session);

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var builder = new BatchBuilder { TenantId = session.TenantId };
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);
        builder.Append(";");

        builder.StartNewCommand();

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        handler.ConfigureCommand(builder, session);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

        return await ReadIntoStreamWithExpectedVersion(session, id, expectedStartingVersion, cancellation, reader, handler).ConfigureAwait(false);
    }


    private async Task<IEventStream<TDoc>> ReadIntoStreamWithExpectedVersion(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation, DbDataReader reader, LoadByIdHandler<TDoc, TId> handler)
    {
        long version = 0;
        try
        {
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


    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, long expectedStartingVersion)
    {
        session.AssertIsDocumentSession();
        var storage = findDocumentStorage(session);
        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        return new WithStartingVersionHandler(this, id, handler, expectedStartingVersion);
    }

    internal class WithStartingVersionHandler: IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchInlinedPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly LoadByIdHandler<TDoc, TId> _handler;
        private readonly long _version;

        public WithStartingVersionHandler(FetchInlinedPlan<TDoc, TId> parent, TId id, LoadByIdHandler<TDoc, TId> handler, long version)
        {
            _parent = parent;
            _id = id;
            _handler = handler;
            _version = version;
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(builder, _id, false);
            builder.StartNewCommand();
            _handler.ConfigureCommand(builder, session);
        }

        #region things we don't care about

        public IEventStream<TDoc> Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotImplementedException();
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        #endregion

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            return _parent.ReadIntoStreamWithExpectedVersion((DocumentSessionBase)session, _id, _version, token, reader, _handler);
        }
    }

}
