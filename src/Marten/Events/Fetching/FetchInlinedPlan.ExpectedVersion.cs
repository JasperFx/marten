using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Services;
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
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, false);
        builder.Append(";");

        builder.StartNewCommand();

        var handler = new LoadByIdHandler<TDoc, TId>((IDocumentStorage<TDoc, TId>)storage, id);
        handler.ConfigureCommand(builder, session);

        InlineRead read;
        await using (var reader =
                     await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            read = await ReadWithExpectedVersion(session, id, expectedStartingVersion, cancellation, reader, handler)
                .ConfigureAwait(false);
        }

        return await buildStreamAsync(session, id, read, (IDocumentStorage<TDoc, TId>)storage, null, default,
            cancellation).ConfigureAwait(false);
    }


    private async Task<InlineRead> ReadWithExpectedVersion(DocumentSessionBase session, TId id, long expectedStartingVersion,
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

            return new InlineRead(version, document);
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
        var handler = new LoadByIdHandler<TDoc, TId>((IDocumentStorage<TDoc, TId>)storage, id);
        return new WithStartingVersionHandler(this, id, storage, handler, expectedStartingVersion);
    }

    internal class WithStartingVersionHandler: IFinishesAfterTheReader<IEventStream<TDoc>>
    {
        private readonly FetchInlinedPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly IDocumentStorage<TDoc, TId> _storage;
        private readonly LoadByIdHandler<TDoc, TId> _handler;
        private readonly long _version;
        private InlineRead _read;

        public WithStartingVersionHandler(FetchInlinedPlan<TDoc, TId> parent, TId id,
            IDocumentStorage<TDoc, TId> storage, LoadByIdHandler<TDoc, TId> handler, long version)
        {
            _parent = parent;
            _id = id;
            _storage = storage;
            _handler = handler;
            _version = version;
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, false);
            builder.StartNewCommand();
            _handler.ConfigureCommand(builder, session);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            _read = await _parent.ReadWithExpectedVersion((DocumentSessionBase)session, _id, _version, token, reader,
                _handler).ConfigureAwait(false);
        }

        public Task<IEventStream<TDoc>> FinishAsync(IStorageSession session, CancellationToken token)
        {
            return _parent.buildStreamAsync((DocumentSessionBase)session, _id, _read, _storage, null, default, token);
        }

        public async Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            await ReadAsync(reader, session, token).ConfigureAwait(false);
            return await FinishAsync(session, token).ConfigureAwait(false);
        }
    }

}
