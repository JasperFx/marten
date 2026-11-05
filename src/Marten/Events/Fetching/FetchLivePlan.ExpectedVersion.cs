using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal partial class FetchLivePlan<TDoc, TId>
{
    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion,
        CancellationToken cancellation = default)
    {

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(IsGlobal, builder, id, false);

        builder.StartNewCommand();
        configureColdBoundary(builder, id);

        var handler = _identityStrategy.BuildEventQueryHandler(IsGlobal, id, selector);
        handler.ConfigureCommand(builder, session);

        LiveRead read;
        await using (var reader =
                     await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false))
        {
            read = await readAsync(reader, session, handler, true, cancellation).ConfigureAwait(false);
        }

        return await buildStreamAsync(session, id, expectedStartingVersion, read, cancellation).ConfigureAwait(false);
    }

    private Task<IEventStream<TDoc>> buildStreamAsync(DocumentSessionBase session, TId id, long expectedStartingVersion,
        LiveRead read, CancellationToken cancellation)
    {
        if (expectedStartingVersion != read.Version)
        {
            throw new ConcurrencyException(
                $"Expected the existing version to be {expectedStartingVersion}, but was {read.Version}",
                typeof(TDoc), id);
        }

        return buildStreamAsync(session, id, read, cancellation);
    }

    public IQueryHandler<IEventStream<TDoc>> BuildQueryHandler(QuerySession session, TId id, long expectedStartingVersion)
    {
        session.AssertIsDocumentSession();
        return new ExpectedVersionQueryHandler(this, id, expectedStartingVersion);
    }

    public class ExpectedVersionQueryHandler: IFinishesAfterTheReader<IEventStream<TDoc>>
    {
        private readonly FetchLivePlan<TDoc, TId> _parent;
        private readonly TId _id;
        private readonly long _expectedStartingVersion;
        private readonly IQueryHandler<IReadOnlyList<IEvent>> _handler;
        private LiveRead _read;

        public ExpectedVersionQueryHandler(FetchLivePlan<TDoc, TId> parent, TId id, long expectedStartingVersion)
        {
            _parent = parent;
            _id = id;
            _expectedStartingVersion = expectedStartingVersion;
            _handler = _parent._identityStrategy.BuildEventQueryHandler(parent.IsGlobal, _id);
        }

        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            _parent._identityStrategy.BuildCommandForReadingVersionForStream(_parent.IsGlobal, builder, _id, false);

            builder.StartNewCommand();
            _parent.configureColdBoundary(builder, _id);

            _handler.ConfigureCommand(builder, session);
        }

        public async Task ReadAsync(DbDataReader reader, IStorageSession session, CancellationToken token)
        {
            _read = await _parent.readAsync(reader, session, _handler, true, token).ConfigureAwait(false);
        }

        public Task<IEventStream<TDoc>> FinishAsync(IStorageSession session, CancellationToken token)
        {
            return _parent.buildStreamAsync((DocumentSessionBase)session, _id, _expectedStartingVersion, _read, token);
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
