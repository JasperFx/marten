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
using Marten.Services;
using Marten.Services.BatchQuerying;
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

        return await withSnapshotRaceRedos(session, id, async detect =>
        {
            var builder = new BatchBuilder{TenantId = session.TenantId};
            var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
            writeReads(builder, session, id, false, loadHandler);

            var batch = builder.Compile();
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            return await ReadIntoStream(session, id, expectedStartingVersion, cancellation, reader, loadHandler,
                selector, detect).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task<IEventStream<TDoc>> ReadIntoStream(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation, DbDataReader reader, LoadByIdHandler<TDoc, TId> loadHandler, IEventStorage selector,
        bool detectSnapshotRace)
    {
        long version = 0;
        try
        {
            // Read the latest version
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            // The events after the snapshot, then the snapshot itself -- if any!
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var delta = await readDeltaAsync(reader, selector, cancellation).ConfigureAwait(false);

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            var snapshotVersion = snapshotVersionFor(session, id);
            var events = eventsAfterSnapshot(id, document != null, snapshotVersion, delta, detectSnapshotRace);

            // Compared with the version the aggregate actually reached, which is past the stream version read
            // first if events were appended in between
            version = reachedVersion(version, document != null, snapshotVersion, events);
            if (expectedStartingVersion != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {expectedStartingVersion}, but was {version}",
                    typeof(TDoc), id);
            }

            _events.Options.OpenTelemetry
                .RecordEventsReplayed(events.Count, _aggregateTypeName, OpenTelemetryOptions.AsyncPlan);

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
            if (((IMartenSession)session).Options.Events.UseIdentityMapForAggregates)
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

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent.writeReads(builder, session, _id, false, _loadHandler);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            // Detection ON: a batch cannot redo itself, so a snapshot that went backwards is refused with
            // SnapshotRaceException rather than answered wrongly. See ForUpdateQueryHandler.
            return _parent.ReadIntoStream(documentSessionBase, _id, _expectedStartingVersion, token, reader,
                _loadHandler, documentSessionBase.EventStorage(), true);
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

    }
}
