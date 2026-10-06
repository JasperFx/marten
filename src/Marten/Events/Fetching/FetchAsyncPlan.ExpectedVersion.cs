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

        var builder = new BatchBuilder{TenantId = session.TenantId};

        // #5604: the same bracket the non-version overload has always had. Without it the three reads
        // below each take their own snapshot, and a daemon snapshot write landing between the second
        // and the third returns a null or stale aggregate for a stream that is perfectly healthy.
        var sharedSnapshot = beginSharedSnapshot(builder, session);

        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, false);

        builder.StartNewCommand();

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        endSharedSnapshot(builder, sharedSnapshot);

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
        return new ExpectedVersionQueryHandler(this, id, expectedStartingVersion, canShareOneSnapshot(dsb));
    }

    public class ExpectedVersionQueryHandler: IQueryHandler<IEventStream<TDoc>>, IOpensItsOwnTransaction
    {
        private readonly FetchAsyncPlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly long _expectedStartingVersion;
        private readonly bool _sharedSnapshot;
        private readonly LoadByIdHandler<TDoc,TId> _loadHandler;

        /// <summary>
        ///     Unlike its non-exclusive sibling this handler has no for-update mode, so it brackets its
        ///     reads whenever it is allowed to at all. #5604 gave it the bracket, and the marker is what
        ///     makes a batch enforce the two rules that come with one — it has to be the batch's first
        ///     operation, and it cannot share a batch with an exclusive fetch whose row lock its <c>end</c>
        ///     would release. Without the marker a batched expected-version fetch would fail at
        ///     <c>Execute()</c> with the bare <c>25001</c> that #5535 was filed to eliminate.
        ///     <para>
        ///     #5611 made it conditional rather than constant: on a session already inside a caller-owned
        ///     transaction there is no bracket, and so nothing for either rule to protect.
        ///     </para>
        /// </summary>
        public bool OpensItsOwnTransaction => _sharedSnapshot;

        public string CallName => "FetchForWriting";

        public ExpectedVersionQueryHandler(FetchAsyncPlan<TDoc,TId> parent, TId id, long expectedStartingVersion,
            bool sharedSnapshot)
        {
            _parent = parent;
            _id = id;
            _expectedStartingVersion = expectedStartingVersion;
            _sharedSnapshot = sharedSnapshot;

            _loadHandler = new LoadByIdHandler<TDoc, TId>(_parent._storage, id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            // Asked again here rather than reusing _sharedSnapshot -- see ForUpdateQueryHandler for why.
            var sharedSnapshot = beginSharedSnapshot(builder, (QuerySession)session);

            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, false);

            builder.StartNewCommand();

            _loadHandler.ConfigureCommand(builder, session);

            builder.StartNewCommand();

            _parent.writeEventFetchStatement(_id, builder);

            endSharedSnapshot(builder, sharedSnapshot);
        }

        public Task<IEventStream<TDoc>> HandleAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            var documentSessionBase = (DocumentSessionBase)session;
            return _parent.ReadIntoStream(documentSessionBase, _id, _expectedStartingVersion, token, reader,
                _loadHandler, documentSessionBase.EventStorage());
        }

        public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
        {
            throw new NotSupportedException();
        }

    }
}
