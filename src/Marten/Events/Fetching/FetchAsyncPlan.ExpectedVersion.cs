using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchAsyncPlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        ensureInitialSql(selector);
        // TODO -- use read only transaction????

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);

        builder.StartNewCommand();

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        var batch = builder.Compile();
        await using var reader =
            await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        return await ReadIntoStream(session, id, expectedStartingVersion, cancellation, reader, loadHandler, selector).ConfigureAwait(false);
    }

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation, DbDataReader reader, LoadByIdHandler<TDoc, TId> loadHandler, IEventStorage selector)
    {
        long version = 0;
        try
        {
            // Read the latest version
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

            // Fetch the existing aggregate -- if any!
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
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
                throw new StreamLockedException(id, e.InnerException!);
            }

            throw;
        }
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, long expectedStartingVersion)
    {
        var dsb = session.AssertIsDocumentSession();
        if (_initialSql.IsEmpty())
        {
            ensureInitialSql(dsb.EventStorage());
        }
        return new ExpectedVersionQueryHandler(this, id, expectedStartingVersion);
    }

    public class ExpectedVersionQueryHandler: IQueryHandler<IEventStream<TDoc>>
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly long _expectedStartingVersion;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        public ExpectedVersionQueryHandler(FetchAsyncPlan<TDoc,TId> parent, TId id, long expectedStartingVersion)
        {
            _parent = parent;
            _id = id;
            _expectedStartingVersion = expectedStartingVersion;

            _loadHandler = new LoadByIdHandler<TDoc, TId>(_parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(builder, _id, false);

            builder.StartNewCommand();

            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            return _parent.ReadIntoStream(documentSessionBase, _id, _expectedStartingVersion, token, reader,
                _loadHandler, documentSessionBase.EventStorage());
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public IEventStream<TDoc> Handle(DbDataReader reader, IMartenSession session)
        {
            throw new NotSupportedException();
        }
    }
}
